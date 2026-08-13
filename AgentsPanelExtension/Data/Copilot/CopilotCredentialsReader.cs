using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace AgentsPanelExtension;

// Discovers a GitHub token that can call the Copilot usage endpoint, probing the places GitHub's own
// tools leave one, at CALL TIME — never cached across polls, never persisted anywhere else, and the
// token value is NEVER logged (log only which source produced one). No source yielding a usable token
// degrades to null, which the provider reports as UsageStatus.NotConfigured.
//
// Probe order (first usable token wins):
//   1. The extension's own optional PAT setting (UsageSettingsManager.CopilotToken) — the explicit
//      override, e.g. a fine-grained PAT with "Copilot Requests: Read".
//   2. Env vars, in Copilot CLI's own precedence order: COPILOT_GITHUB_TOKEN → GH_TOKEN → GITHUB_TOKEN.
//   3. Windows Credential Manager: Copilot CLI ("<uuid>.github-copilot-app" — suffix-matched via a
//      full enumerate, since CredEnumerate wildcards are prefix-only) then gh CLI secure storage
//      ("gh:github.com:<user>", go-keyring "service:user" naming — target names observed live).
//   4. gh CLI's plaintext fallback %APPDATA%\GitHub CLI\hosts.yml (GH_CONFIG_DIR honored).
//   5. The editor Copilot plugins' %LOCALAPPDATA%\github-copilot\apps.json / hosts.json.
//
// Classic PATs (ghp_) are skipped wherever found — the endpoint rejects them outright; ghu_/gho_
// OAuth tokens and fine-grained PATs work. No token refresh here by design (same stance as the
// Claude/Codex readers): these are long-lived OAuth tokens owned by other apps; a rejected one is
// simply reported (TokenExpired) and re-auth happens in the owning tool.
internal static class CopilotCredentialsReader
{
    private const string Tag = "CopilotCreds";

    // The token lives in this record only for the duration of one fetch — request scope, nothing
    // longer. Source is a human-readable label for logs/diagnostics, never the token itself.
    internal sealed record CopilotCredentials(string Token, string Source);

    public static CopilotCredentials? Read()
    {
        try
        {
            var credentials =
                FromSetting()
                ?? FromEnvironment()
                ?? FromCredentialManager()
                ?? FromGhHostsFile()
                ?? FromCopilotAppFiles();
            Log.Info(Tag, credentials is null ? "no usable token found" : $"token found via {credentials.Source}");
            return credentials;
        }
        catch (Exception ex)
        {
            Log.Error(Tag, "credential discovery failed", ex);
            return null;
        }
    }

    private static CopilotCredentials? FromSetting()
    {
        var token = UsageSettingsManager.Instance.CopilotToken;
        return Usable(token, "settings") ? new CopilotCredentials(token!.Trim(), "settings PAT") : null;
    }

    private static CopilotCredentials? FromEnvironment()
    {
        foreach (var name in (string[])["COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN"])
        {
            var token = Environment.GetEnvironmentVariable(name);
            if (Usable(token, name))
                return new CopilotCredentials(token!.Trim(), $"env {name}");
        }

        return null;
    }

    private static CopilotCredentials? FromCredentialManager()
    {
        // One walk over the user's generic credentials, matched by target-name SHAPE (observed live
        // 2026-08 on this machine): Copilot CLI stores "<uuid>.github-copilot-app" — a suffix match,
        // and CredEnumerate wildcards are prefix-only, hence the full walk — and gh's secure storage
        // "gh:github.com:<user>" (go-keyring "service:user" naming; the user half varies, so an
        // exact CredRead misses it). Copilot CLI's own token wins over gh's when both exist.
        try
        {
            if (!NativeMethods.CredEnumerate(null, 0, out var count, out var array))
                return null;
            try
            {
                CopilotCredentials? ghCredentials = null;
                for (var i = 0; i < count; i++)
                {
                    var credential = Marshal.PtrToStructure<NativeMethods.Credential>(
                        Marshal.ReadIntPtr(array, i * IntPtr.Size));
                    if (credential.Type != CredTypeGeneric)
                        continue;
                    var target = Marshal.PtrToStringUni(credential.TargetName);
                    if (target is null)
                        continue;

                    if (target.EndsWith(".github-copilot-app", StringComparison.OrdinalIgnoreCase)
                        || target.StartsWith("copilot-cli", StringComparison.OrdinalIgnoreCase))
                    {
                        var token = ExtractToken(DecodeBlob(credential));
                        if (Usable(token, "Credential Manager (Copilot CLI)"))
                            return new CopilotCredentials(token!, "Credential Manager (Copilot CLI)");
                    }
                    else if (ghCredentials is null
                        && target.StartsWith("gh:github.com", StringComparison.OrdinalIgnoreCase))
                    {
                        var token = ExtractToken(DecodeBlob(credential));
                        if (Usable(token, "Credential Manager (gh)"))
                            ghCredentials = new CopilotCredentials(token!, "Credential Manager (gh)");
                    }
                }

                return ghCredentials;
            }
            finally
            {
                NativeMethods.CredFree(array);
            }
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"Credential Manager walk failed: {ex.GetType().Name}");
            return null;
        }
    }

    // A blob may hold the raw token or a small JSON wrapper around it (unverified for the Copilot
    // CLI's store) — probe the common field names rather than sending JSON as a Bearer value.
    private static string? ExtractToken(string? blob)
    {
        if (blob is null || !blob.StartsWith('{'))
            return blob;
        try
        {
            using var document = JsonDocument.Parse(blob);
            foreach (var name in (string[])["oauth_token", "token", "access_token"])
            {
                if (document.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
            }
        }
        catch (JsonException)
        {
        }

        Log.Warn(Tag, "credential blob looked like JSON but held no recognizable token field");
        return null;
    }

    // gh CLI's plaintext token store (used when secure storage was declined/unavailable). Minimal
    // hand parse instead of a YAML dependency: a non-indented "github.com:" line opens the host
    // block; the block's indented "oauth_token: x" line carries the token. Secure-storage installs
    // keep hosts.yml around without an oauth_token line, so falling through here is common.
    private static CopilotCredentials? FromGhHostsFile()
    {
        try
        {
            var dir = Environment.GetEnvironmentVariable("GH_CONFIG_DIR");
            if (string.IsNullOrWhiteSpace(dir))
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitHub CLI");
            var path = Path.Combine(dir, "hosts.yml");
            if (!File.Exists(path))
                return null;

            string? host = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                if (raw.Length == 0)
                    continue;
                if (!char.IsWhiteSpace(raw[0]))
                {
                    host = raw.Trim().TrimEnd(':');
                    continue;
                }

                if (host != "github.com")
                    continue;
                var line = raw.Trim();
                if (!line.StartsWith("oauth_token:", StringComparison.Ordinal))
                    continue;
                var token = line["oauth_token:".Length..].Trim().Trim('"', '\'');
                if (Usable(token, "gh hosts.yml"))
                    return new CopilotCredentials(token, "gh hosts.yml");
            }
        }
        catch (Exception ex)
        {
            // Deliberately does not include file contents — only the exception itself.
            Log.Warn(Tag, $"failed to read gh hosts.yml: {ex.GetType().Name}");
        }

        return null;
    }

    // The editor plugins' credential files (JetBrains/Neovim/older VS Code Copilot). apps.json keys
    // are "github.com:<clientId>", hosts.json keys are plain "github.com" — same entry shape, so one
    // dictionary DTO covers both, matched by key prefix.
    private static CopilotCredentials? FromCopilotAppFiles()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "github-copilot");
        foreach (var file in (string[])["apps.json", "hosts.json"])
        {
            try
            {
                var path = Path.Combine(dir, file);
                if (!File.Exists(path))
                    continue;
                var entries = JsonSerializer.Deserialize(
                    File.ReadAllText(path), CopilotJsonContext.Default.DictionaryStringApiCopilotAppEntryDto);
                if (entries is null)
                    continue;
                foreach (var (key, entry) in entries)
                {
                    if (!key.StartsWith("github.com", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var token = entry?.OAuthToken;
                    if (Usable(token, file))
                        return new CopilotCredentials(token!.Trim(), $"github-copilot {file}");
                }
            }
            catch (Exception ex)
            {
                Log.Warn(Tag, $"failed to read {file}: {ex.GetType().Name}");
            }
        }

        return null;
    }

    // A token worth trying: non-empty, no interior whitespace (a decoded blob that isn't actually a
    // bare token must not become a Bearer value), and not a classic PAT — the usage endpoint rejects
    // ghp_ outright ("Personal Access Tokens are not supported"), so skipping keeps the probe going
    // instead of surfacing a guaranteed 403.
    private static bool Usable(string? token, string source)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;
        var trimmed = token.Trim();
        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                Log.Warn(Tag, $"{source}: value with embedded whitespace skipped — not a bare token");
                return false;
            }
        }

        if (trimmed.StartsWith("ghp_", StringComparison.Ordinal))
        {
            Log.Info(Tag, $"{source}: classic PAT (ghp_) skipped — the usage endpoint rejects them");
            return false;
        }

        return true;
    }

    // --- Windows Credential Manager (advapi32) ---------------------------------------------------

    private const uint CredTypeGeneric = 1;

    // Credential blobs have no declared encoding: PowerShell/.NET writers use UTF-16LE, Go writers
    // (gh, Copilot CLI) UTF-8. GitHub tokens are ASCII, so UTF-16 is recognizable by the zeroed high
    // byte of the first character.
    private static string? DecodeBlob(in NativeMethods.Credential credential)
    {
        if (credential.CredentialBlob == 0 || credential.CredentialBlobSize == 0)
            return null;
        var bytes = new byte[credential.CredentialBlobSize];
        Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
        var text = bytes.Length >= 2 && bytes.Length % 2 == 0 && bytes[1] == 0
            ? Encoding.Unicode.GetString(bytes)
            : Encoding.UTF8.GetString(bytes);
        return text.Trim('\0').Trim();
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct Credential
        {
            public uint Flags;
            public uint Type;
            public nint TargetName;
            public nint Comment;
            public long LastWritten;   // FILETIME
            public uint CredentialBlobSize;
            public nint CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public nint Attributes;
            public nint TargetAlias;
            public nint UserName;
        }

        [DllImport("advapi32", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool CredEnumerate(string? filter, uint flags, out uint count, out nint credentials);

        [DllImport("advapi32", EntryPoint = "CredFree")]
        internal static extern void CredFree(nint buffer);
    }
}
