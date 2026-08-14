using System;

namespace AgentsPanelExtension;

// The one honest self-identification string every provider HTTP client sends (deliberate decision:
// no client spoofing, no Editor-Version masquerades). The version rides in from the assembly —
// csproj <Version> mirrors <AppxPackageVersion>, so a release bumps the csproj once and the UA
// follows automatically. Never hardcode a version literal here or in a provider.
internal static class AppInfo
{
    // Three-part ("agents-panel/0.1.0"), matching the historical literal the endpoints have seen.
    public static readonly string UserAgent =
        typeof(AppInfo).Assembly.GetName().Version is { } v
            ? $"agents-panel/{v.Major}.{v.Minor}.{v.Build}"
            : "agents-panel";
}
