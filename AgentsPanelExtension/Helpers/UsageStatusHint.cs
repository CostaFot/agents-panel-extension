using AgentsPanelExtension.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AgentsPanelExtension;

// Status rows explaining the current data state — the ApiKeyHint pattern from MarketExtension: helpers
// return an IListItem? and callers append only when non-null:
//
//     if (UsageStatusHint.DemoRow() is { } hint) items.Add(hint);
//
// Colors follow the same convention: blue = deliberate state (demo mode), red = broken (not signed in /
// expired), amber = degraded but self-healing (rate-limited / transient error). A ListItem has no
// per-row text-color lever; a colored Tag (pill) is the only way to tint a row.
internal static class UsageStatusHint
{
    private const string WarningGlyph = "\uE7BA"; // Segoe MDL2 Warning
    private const string InfoGlyph = "\uE946";    // Segoe MDL2 Info

    private static OptionalColor WarningRed => ColorHelpers.FromRgb(0xD1, 0x34, 0x38);
    private static OptionalColor DegradedAmber => ColorHelpers.FromRgb(0xF7, 0x63, 0x0C);
    private static OptionalColor DemoBlue => ColorHelpers.FromRgb(0x00, 0x78, 0xD4);

    // Built once and reused: the toolkit's navigable settings form over our settings singleton.
    private static IContentPage? _settingsPage;

    private static IContentPage SettingsPage =>
        _settingsPage ??= UsageSettingsManager.Instance.Settings.SettingsPage;

    // "Demo mode — showing sample data": surfaced while the Demo-mode setting is on so it's obvious the
    // numbers are simulated. Enter → Settings (to turn it off). Null when live.
    public static IListItem? DemoRow() =>
        UsageSettingsManager.Instance.DemoMode
            ? new ListItem(SettingsPage)
            {
                Title = Resources.Status_Demo_Title,
                Subtitle = Resources.Status_Demo_Subtitle,
                Icon = new IconInfo(InfoGlyph),
                Tags = [new Tag(Resources.Status_Demo_Tag) { Foreground = DemoBlue }],
            }
            : null;

    // The per-snapshot problem row, or null when the snapshot is Ok. Shown INSTEAD of window rows when
    // there's nothing to show (never signed in), or UNDER them when stale numbers survived
    // (keep-last-good: rate-limited/error/expired with old windows still rendering above).
    public static IListItem? StatusRow(DomainUsageSnapshot snapshot)
    {
        var name = snapshot.ProviderDisplayName;
        return snapshot.Status switch
        {
            UsageStatus.Ok => null,
            UsageStatus.NotConfigured => Row(
                Strings.Format(Resources.Status_NotSignedIn_Title, name),
                Resources.Status_NotSignedIn_Subtitle,
                Resources.Status_NotSignedIn_Tag, WarningRed,
                $"com.costafotiadis.agentspanel.status.{snapshot.ProviderId}.signin"),
            UsageStatus.TokenExpired => Row(
                Strings.Format(Resources.Status_TokenExpired_Title, name),
                Resources.Status_TokenExpired_Subtitle,
                Resources.Status_TokenExpired_Tag, WarningRed,
                $"com.costafotiadis.agentspanel.status.{snapshot.ProviderId}.expired"),
            UsageStatus.RateLimited => Row(
                Strings.Format(Resources.Status_RateLimited_Title, name),
                Resources.Status_RateLimited_Subtitle,
                Resources.Status_RateLimited_Tag, DegradedAmber,
                $"com.costafotiadis.agentspanel.status.{snapshot.ProviderId}.ratelimited"),
            _ => Row(
                Strings.Format(Resources.Status_Error_Title, name),
                Resources.Status_Error_Subtitle,
                Resources.Status_Error_Tag, DegradedAmber,
                $"com.costafotiadis.agentspanel.status.{snapshot.ProviderId}.error"),
        };
    }

    // Status rows are informational — there is nothing useful for Enter to do (we can't sign the user
    // in), so a NoOpCommand with a stable non-empty Id.
    private static ListItem Row(string title, string subtitle, string tag, OptionalColor color, string id) =>
        new(new NoOpCommand { Id = id })
        {
            Title = title,
            Subtitle = subtitle,
            Icon = new IconInfo(WarningGlyph),
            Tags = [new Tag(tag) { Foreground = color }],
        };
}
