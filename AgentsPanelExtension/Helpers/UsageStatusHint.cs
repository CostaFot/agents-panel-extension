using AgentsPanelExtension.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AgentsPanelExtension;

// Status rows explaining the current data state — the ApiKeyHint pattern from MarketExtension: helpers
// return an IListItem? and callers append only when non-null:
//
//     if (UsageStatusHint.StatusRow(snapshot) is { } hint) items.Add(hint);
//
// Colors follow the same convention: red = broken (not signed in / expired), amber = degraded but
// self-healing (rate-limited / transient error). A ListItem has no
// per-row text-color lever; a colored Tag (pill) is the only way to tint a row.
internal static class UsageStatusHint
{
    private const string WarningGlyph = "\uE7BA"; // Segoe MDL2 Warning

    private static OptionalColor WarningRed => ColorHelpers.FromRgb(0xD1, 0x34, 0x38);
    private static OptionalColor DegradedAmber => ColorHelpers.FromRgb(0xF7, 0x63, 0x0C);

    // The severity pill for a degraded status (text + color, same red/amber convention as StatusRow);
    // null for Ok. The hub's provider rows use this to flag a problem without the whole status row.
    public static Tag? StatusTag(UsageStatus status) => status switch
    {
        UsageStatus.Ok => null,
        UsageStatus.NotConfigured => new Tag(Resources.Status_NotSignedIn_Tag) { Foreground = WarningRed },
        UsageStatus.TokenExpired => new Tag(Resources.Status_TokenExpired_Tag) { Foreground = WarningRed },
        UsageStatus.RateLimited => new Tag(Resources.Status_RateLimited_Tag) { Foreground = DegradedAmber },
        _ => new Tag(Resources.Status_Error_Tag) { Foreground = DegradedAmber },
    };

    // The one-line explanation for a degraded status — the hub row's subtitle when a provider has no
    // windows to summarize; null for Ok.
    public static string? StatusSubtitle(UsageStatus status) => status switch
    {
        UsageStatus.Ok => null,
        UsageStatus.NotConfigured => Resources.Status_NotSignedIn_Subtitle,
        UsageStatus.TokenExpired => Resources.Status_TokenExpired_Subtitle,
        UsageStatus.RateLimited => Resources.Status_RateLimited_Subtitle,
        _ => Resources.Status_Error_Subtitle,
    };

    // The per-snapshot problem row, or null when the snapshot is Ok. Shown INSTEAD of window rows when
    // there's nothing to show (never signed in), or UNDER them when stale numbers survived
    // (keep-last-good: rate-limited/error/expired with old windows still rendering above).
    public static IListItem? StatusRow(DomainUsageSnapshot snapshot)
    {
        var name = snapshot.ProviderDisplayName;
        return snapshot.Status switch
        {
            UsageStatus.Ok => null,
            UsageStatus.NotConfigured => Row(snapshot,
                Strings.Format(Resources.Status_NotSignedIn_Title, name), "signin"),
            UsageStatus.TokenExpired => Row(snapshot,
                Strings.Format(Resources.Status_TokenExpired_Title, name), "expired"),
            UsageStatus.RateLimited => Row(snapshot,
                Strings.Format(Resources.Status_RateLimited_Title, name), "ratelimited"),
            _ => Row(snapshot,
                Strings.Format(Resources.Status_Error_Title, name), "error"),
        };
    }

    // Status rows are informational — there is nothing useful for Enter to do (we can't sign the user
    // in), so a NoOpCommand with a stable non-empty Id. Subtitle and pill come from the per-status
    // helpers above so the text/colors stay defined once.
    private static ListItem Row(DomainUsageSnapshot snapshot, string title, string kind) =>
        new(new NoOpCommand { Id = $"com.costafotiadis.agentspanel.status.{snapshot.ProviderId}.{kind}" })
        {
            Title = title,
            Subtitle = StatusSubtitle(snapshot.Status) ?? string.Empty,
            Icon = new IconInfo(WarningGlyph),
            Tags = StatusTag(snapshot.Status) is { } tag ? [tag] : [],
        };
}
