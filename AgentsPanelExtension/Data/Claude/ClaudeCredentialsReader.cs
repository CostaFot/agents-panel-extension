using System;
using System.IO;
using System.Text.Json;

namespace AgentsPanelExtension;

// Reads Claude Code's local OAuth credentials at CALL TIME — never cached across polls, never
// persisted anywhere else, and the token value is NEVER logged (log only presence/expiry booleans).
// Missing file / unparseable JSON / empty token all degrade to null, which the provider reports as
// UsageStatus.NotConfigured ("open Claude Code and sign in").
//
// No token refresh here by design: rotation of Claude Code's refresh token is undocumented, and a
// botched refresh could invalidate the session and log the user out of Claude Code itself. An expired
// access token is simply reported (TokenExpired) — using Claude Code refreshes it as a side effect.
internal static class ClaudeCredentialsReader
{
    private const string Tag = "ClaudeCreds";

    // The subset of the credentials file the provider needs. The token lives in this record only for
    // the duration of one fetch — request scope, nothing longer.
    internal sealed record ClaudeCredentials(
        string AccessToken,
        DateTimeOffset? ExpiresAt,
        string? SubscriptionType,
        string? RateLimitTier)
    {
        public bool IsExpired => ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow;
    }

    private static string CredentialsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    // Cheap existence probe for IsAvailable-style checks that mustn't read the file.
    public static bool CredentialsFileExists => File.Exists(CredentialsPath);

    public static ClaudeCredentials? Read()
    {
        try
        {
            if (!File.Exists(CredentialsPath))
            {
                Log.Info(Tag, "credentials file not found");
                return null;
            }

            var dto = JsonSerializer.Deserialize(
                File.ReadAllText(CredentialsPath), ClaudeJsonContext.Default.ApiClaudeCredentialsFileDto);
            var oauth = dto?.ClaudeAiOauth;
            if (string.IsNullOrWhiteSpace(oauth?.AccessToken))
            {
                Log.Warn(Tag, "credentials file present but no access token in it");
                return null;
            }

            var credentials = new ClaudeCredentials(
                oauth.AccessToken, ToExpiry(oauth.ExpiresAt), oauth.SubscriptionType, oauth.RateLimitTier);
            Log.Info(Tag, $"credentials read (expired={credentials.IsExpired}, plan={credentials.SubscriptionType ?? "?"})");
            return credentials;
        }
        catch (Exception ex)
        {
            // Deliberately does not include file contents — only the exception itself.
            Log.Error(Tag, "failed to read credentials file", ex);
            return null;
        }
    }

    // expiresAt is epoch MILLISECONDS in the observed file format; defensively treat a value that is
    // too small to be milliseconds (before ~2001 as ms) as epoch seconds instead.
    private static DateTimeOffset? ToExpiry(long? raw) =>
        raw is not { } value || value <= 0
            ? null
            : value < 1_000_000_000_000
                ? DateTimeOffset.FromUnixTimeSeconds(value)
                : DateTimeOffset.FromUnixTimeMilliseconds(value);
}
