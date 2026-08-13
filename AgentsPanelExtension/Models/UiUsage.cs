using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AgentsPanelExtension.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AgentsPanelExtension;

// Presentation projection of one quota window. THE ONLY PLACE FORMATTING LIVES (with UiUsage below) —
// pages and the dock bind to these so Domain stays formatting-free, mirroring MarketExtension's UiQuote.
internal sealed record UiUsageWindow(DomainUsageWindow Window)
{
    // Windows severity colors for the percent pill: a quota near its cap should read as a problem.
    private static OptionalColor SeverityGreen => ColorHelpers.FromRgb(0x10, 0x7C, 0x10);
    private static OptionalColor SeverityAmber => ColorHelpers.FromRgb(0xF7, 0x63, 0x0C);
    private static OptionalColor SeverityRed => ColorHelpers.FromRgb(0xD1, 0x34, 0x38);

    // Compact label for dock buttons ("5h", "Wk", a model name, "Extra") — the dock title budget is
    // ~15 chars total, so these stay tiny.
    public string ShortLabel => Window.Kind switch
    {
        UsageWindowKind.Session => Resources.Window_Session_Short,
        UsageWindowKind.Week => Resources.Window_Week_Short,
        UsageWindowKind.ModelWeek => Window.Qualifier ?? Resources.Window_Week_Short,
        UsageWindowKind.ExtraUsage => Resources.Window_Extra_Short,
        _ => Window.Id,
    };

    // Full label for hub rows ("Session (5-hour)", "Weekly — Opus", ...).
    public string LongLabel => Window.Kind switch
    {
        UsageWindowKind.Session => Resources.Window_Session_Long,
        UsageWindowKind.Week => Resources.Window_Week_Long,
        UsageWindowKind.ModelWeek => Strings.Format(Resources.Window_ModelWeek_Long, Window.Qualifier ?? "?"),
        UsageWindowKind.ExtraUsage => Resources.Window_Extra_Long,
        _ => Window.Id,
    };

    // "23%" — whole percent, clamped upstream. Invariant digits (a percentage is a percentage).
    public string FormatPercent() =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(Window.Utilization)}%");

    // The dock button title, e.g. "5h 23%" / "Wk 41%" / "Opus 67%" — must stay within the host's
    // ~15-char button budget (longer titles get ellipsized, not scrolled).
    public string DockTitle() => $"{ShortLabel} {FormatPercent()}";

    // The secondary line: when the window resets — "resets 17:30" today, "resets Thu 09:00" further
    // out — or, for the credits window, "$6.00 of $50.00 used". Empty when there's nothing to say.
    public string FormatReset(DateTimeOffset now)
    {
        if (Window.Kind == UsageWindowKind.ExtraUsage)
        {
            return Window is { Used: { } used, Limit: { } limit }
                ? Strings.Format(Resources.Extra_UsedOfLimit, FormatDollars(used), FormatDollars(limit))
                : string.Empty;
        }

        if (Window.ResetsAt is not { } resetsAt)
            return string.Empty;

        var local = resetsAt.ToLocalTime();
        var when = resetsAt - now < TimeSpan.FromHours(24)
            ? local.ToString("t", CultureInfo.CurrentCulture)                          // "17:30"
            : $"{local.ToString("ddd", CultureInfo.CurrentCulture)} {local.ToString("t", CultureInfo.CurrentCulture)}"; // "Thu 09:00"
        return Strings.Format(Resources.Reset_At, when);
    }

    // Percent pill color by how close the window is to its cap.
    public OptionalColor SeverityColor() => Window.Utilization switch
    {
        >= 80 => SeverityRed,
        >= 50 => SeverityAmber,
        _ => SeverityGreen,
    };

    // Currency display for extra-usage credits. The endpoint reports plain numbers with a separate
    // currency field we currently assume USD (verified default); revisit if other currencies appear.
    private static string FormatDollars(decimal amount) =>
        string.Create(CultureInfo.InvariantCulture, $"${amount:0.00}");
}

// Presentation projection of one provider snapshot: ordered windows + status/stale text.
internal sealed record UiUsage(DomainUsageSnapshot Snapshot)
{
    // Session, Week, ModelWeek*, ExtraUsage — enum declaration order is the display order.
    public IReadOnlyList<UiUsageWindow> Windows { get; } =
        [.. Snapshot.Windows.OrderBy(w => w.Kind).Select(w => new UiUsageWindow(w))];

    // The windows that survive the ShowModelWindows / ShowExtraUsage settings filters — the single home
    // for that filter, shared by the hub summary, the provider page rows, and the dock buttons.
    public IEnumerable<UiUsageWindow> VisibleWindows(UsageSettingsManager settings) =>
        Windows.Where(w =>
            (w.Window.Kind != UsageWindowKind.ModelWeek || settings.ShowModelWindows)
            && (w.Window.Kind != UsageWindowKind.ExtraUsage || settings.ShowExtraUsage));

    // One-line summary for the provider's hub row: the dock titles joined — "5h 23% · Wk 41%".
    // Null when no windows survive the filter, so callers can fall back to status/empty text.
    public string? SummaryText(UsageSettingsManager settings)
    {
        var summary = string.Join(" · ", VisibleWindows(settings).Select(w => w.DockTitle()));
        return summary.Length > 0 ? summary : null;
    }

    // The highest-utilization visible window — drives the hub row's severity-colored percent pill.
    public UiUsageWindow? WorstVisibleWindow(UsageSettingsManager settings) =>
        VisibleWindows(settings).OrderByDescending(w => w.Window.Utilization).FirstOrDefault();

    // "stale · as of 17:02" — shown on window rows when the numbers survived a failed refresh
    // (keep-last-good); null when the snapshot is fresh.
    public string? StaleText()
    {
        if (!Snapshot.IsStale)
            return null;
        var asOf = Snapshot.FetchedAt?.ToLocalTime().ToString("t", CultureInfo.CurrentCulture) ?? "?";
        return Strings.Format(Resources.Stale_AsOf, asOf);
    }

    public static UiUsage From(DomainUsageSnapshot snapshot) => new(snapshot);
}
