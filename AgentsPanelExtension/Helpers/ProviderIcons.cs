using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AgentsPanelExtension;

// ProviderId → icon, so a Claude dock button and a Codex dock button are tellable apart at a glance
// (the terse "5h 23%" titles carry no provider identity). A UI-layer map keyed by the snapshot's
// ProviderId — deliberately NOT a member on IAgentUsageProvider or DomainUsageSnapshot, which keeps
// Domain provider-agnostic and pages working from snapshots alone. New provider → add a PNG to
// Assets\ (the csproj globs Assets\**\*.png) and a case here; unknown ids fall back to the
// extension's own logo.
internal static class ProviderIcons
{
    private static readonly IconInfo Claude = IconHelpers.FromRelativePath("Assets\\provider_claude.png");
    private static readonly IconInfo Codex = IconHelpers.FromRelativePath("Assets\\provider_codex.png");
    private static readonly IconInfo Copilot = IconHelpers.FromRelativePath("Assets\\provider_copilot.png");
    private static readonly IconInfo Fallback = IconHelpers.FromRelativePath("Assets\\agentspanel_logo_base_square.png");

    public static IconInfo For(string providerId) => providerId switch
    {
        "claude" => Claude,
        "codex" => Codex,
        "copilot" => Copilot,
        _ => Fallback,
    };
}
