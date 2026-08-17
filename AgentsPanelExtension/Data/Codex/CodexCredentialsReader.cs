using System;
using System.IO;
using System.Text.Json;

namespace AgentsPanelExtension;

// Reads the Codex CLI / desktop app's local OAuth credentials at CALL TIME — never cached across
// polls, never persisted anywhere else, and the token value is NEVER logged (log only
// presence/expiry booleans). Missing file / unparseable JSON / empty token all degrade to null,
// which the provider reports as UsageStatus.NotConfigured ("sign in via Codex").
//
// No token refresh here by design — and for Codex it's a harder no than for Claude: the refresh
// endpoint ROTATES the refresh token, and Codex persists the rotated triple back to auth.json. A
// third-party refresh that doesn't write back perfectly (racing the CLI's own writes) leaves a dead
// refresh token behind and logs the user out of Codex itself. An expired access token is simply
// reported (TokenExpired) — using Codex refreshes it as a side effect (~8-day proactive cadence).
internal static class CodexCredentialsReader
{
    private const string Tag = "CodexCreds";

    // The subset of auth.json the provider needs. The token lives in this record only for the
    // duration of one fetch — request scope, nothing longer.
    internal sealed record CodexCredentials(
        string AccessToken,
        string? AccountId,
        DateTimeOffset? ExpiresAt,
        string? PlanTypeHint)
    {
        public bool IsExpired => ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow;
    }

    // Codex honors CODEX_HOME as the config root; default ~/.codex (same probe pattern as Claude's
    // ~/.claude). Read per call so an env change applies without a reload. Shared with
    // CodexConfigReader — config.toml lives beside auth.json.
    internal static string HomeDirectory
    {
        get
        {
            var home = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(home))
                home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            return home;
        }
    }

    private static string CredentialsPath => Path.Combine(HomeDirectory, "auth.json");

    public static CodexCredentials? Read()
    {
        try
        {
            if (!File.Exists(CredentialsPath))
            {
                Log.Info(Tag, "auth.json not found");
                return null;
            }

            var dto = JsonSerializer.Deserialize(
                File.ReadAllText(CredentialsPath), CodexJsonContext.Default.ApiCodexAuthFileDto);
            var tokens = dto?.Tokens;
            if (string.IsNullOrWhiteSpace(tokens?.AccessToken))
            {
                // Covers API-key-only auth modes too: no ChatGPT tokens means no subscription
                // limits to read, which for this extension is "not signed in".
                Log.Warn(Tag, $"auth.json present but no ChatGPT access token (auth_mode={dto?.AuthMode ?? "?"})");
                return null;
            }

            // Expiry and plan type both live in the access token's JWT claims — no extra call, no
            // id_token parsing needed. A malformed JWT degrades to null claims (never-expiring,
            // unknown plan), not a read failure: the endpoint will still 401 if the token is bad.
            var payload = DecodeJwtPayload(tokens.AccessToken);
            var expiresAt = payload?.Exp is { } exp and > 0 ? DateTimeOffset.FromUnixTimeSeconds(exp) : (DateTimeOffset?)null;
            var credentials = new CodexCredentials(
                tokens.AccessToken, tokens.AccountId, expiresAt, payload?.Auth?.ChatGptPlanType);
            Log.Info(Tag, $"credentials read (expired={credentials.IsExpired}, plan={credentials.PlanTypeHint ?? "?"})");
            return credentials;
        }
        catch (Exception ex)
        {
            // Deliberately does not include file contents — only the exception itself.
            Log.Error(Tag, "failed to read auth.json", ex);
            return null;
        }
    }

    // Decode a JWT's payload segment (base64url, RFC 7515) without signature validation — we're
    // reading claims out of a locally trusted file, not authenticating anything.
    private static ApiCodexJwtPayloadDto? DecodeJwtPayload(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
                return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            var padded = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            return JsonSerializer.Deserialize(
                Convert.FromBase64String(padded), CodexJsonContext.Default.ApiCodexJwtPayloadDto);
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"access token payload didn't decode as a JWT: {ex.GetType().Name}");
            return null;
        }
    }
}
