using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AgentsPanelExtension;

// Reads the one key of the Codex CLI's config.toml this extension honors: chatgpt_base_url — the
// backend base the CLI itself talks to. Self-hosted/enterprise deployments point it away from
// chatgpt.com, and without honoring it the usage fetch would silently 404 against the default
// host. Read at CALL TIME like the credentials, so a config change applies on the next poll.
//
// Parse-with-fallback, no TOML library: only a top-level `chatgpt_base_url = "..."` line counts —
// the scan stops at the first [table] header so a same-named key inside [model_providers.*] can
// never win. Missing file / no match / invalid URL all degrade to null (default base), never
// throws. Only the override's HOST is ever logged, not the full URL.
internal static partial class CodexConfigReader
{
    private const string Tag = "CodexConfig";

    private static string ConfigPath => Path.Combine(CodexCredentialsReader.HomeDirectory, "config.toml");

    [GeneratedRegex("""^\s*chatgpt_base_url\s*=\s*"([^"]+)"\s*(?:#.*)?$""")]
    private static partial Regex BaseUrlLine();

    public static string? ReadChatGptBaseUrl()
    {
        try
        {
            var path = ConfigPath;
            if (!File.Exists(path))
                return null;

            foreach (var line in File.ReadLines(path))
            {
                if (line.TrimStart().StartsWith('['))
                    return null; // first table header — the top-level section is over

                var match = BaseUrlLine().Match(line);
                if (!match.Success)
                    continue;

                var value = match.Groups[1].Value;
                if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                {
                    Log.Info(Tag, $"chatgpt_base_url override → host {uri.Host}");
                    return value;
                }

                Log.Warn(Tag, "chatgpt_base_url present but not a valid http(s) URL — using default base");
                return null;
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"config.toml read failed ({ex.GetType().Name}) — using default base");
            return null;
        }
    }
}
