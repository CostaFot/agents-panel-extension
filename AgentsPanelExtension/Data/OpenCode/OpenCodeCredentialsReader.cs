using System;
using System.IO;
using System.Text.Json;

namespace AgentsPanelExtension;

// Answers ONE question for the opencode provider: is this machine signed in (auth.json has an
// "opencode" entry) or not — the NotConfigured/Ok pivot. Deliberately NOTHING more: unlike the
// other credential readers this one never returns a token, because v1 has no endpoint to spend it
// on (opencode exposes no balance/usage API — see OpenCodeUsageProvider). The entry's key/token
// values are never extracted from the parsed document, let alone logged.
//
// auth.json shape (opencode's packages/opencode/src/auth/index.ts): a map of
// providerID → { "type": "api" | "oauth" | "wellknown", ... }. A Zen login is an entry under the
// "opencode" key (type "api" today); any entry type counts as signed in here.
internal static class OpenCodeCredentialsReader
{
    private const string Tag = "OpenCode";

    // opencode uses xdg-style paths even on Windows (verified live: %USERPROFILE%\.local\share\
    // opencode). XDG_DATA_HOME is honored, read per call so an env change applies without a reload.
    public static string DataDirectory
    {
        get
        {
            var baseDir = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(baseDir, "opencode");
        }
    }

    // Read at call time (never cached) so `opencode auth login`/`logout` applies on the next poll.
    // Any failure (missing file, malformed JSON) → not configured.
    public static bool IsConfigured()
    {
        try
        {
            var path = Path.Combine(DataDirectory, "auth.json");
            if (!File.Exists(path))
            {
                Log.Info(Tag, "auth.json not found — not signed in");
                return false;
            }

            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var configured = document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("opencode", out var entry)
                && entry.ValueKind == JsonValueKind.Object;
            if (!configured)
                Log.Info(Tag, "auth.json has no opencode entry — not signed in");
            return configured;
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"auth.json read failed — treating as not signed in ({ex.GetType().Name}: {ex.Message})");
            return false;
        }
    }
}
