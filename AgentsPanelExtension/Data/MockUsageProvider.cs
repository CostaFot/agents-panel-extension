using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgentsPanelExtension;

// The demo-mode data source: a full IAgentUsageProvider with zero network and zero credentials, so the
// whole UI (hub rows, dock buttons, severity colors, reset countdowns) can be exercised offline.
// Modeled on MarketExtension's MockMarketDataProvider: gated on the Demo-mode setting, and EXCLUSIVE
// while it's on, so it takes over from the real providers entirely (their numbers would be mixed with
// fake ones otherwise). Both gates read the setting live, so a toggle flip applies on the next fetch —
// and UsageRepository's DemoModeChanged subscription makes that fetch happen immediately.
//
// The percentages are fixed, hand-tuned values chosen to exercise each severity band (green / amber /
// red once ShowModelWindows is on); the reset times are anchored to the current clock so countdown
// text looks alive across refreshes.
internal sealed class MockUsageProvider : IAgentUsageProvider
{
    public string Id => "demo";

    public string DisplayName => "Demo";

    public bool IsAvailable => UsageSettingsManager.Instance.DemoMode;

    public bool IsExclusive => UsageSettingsManager.Instance.DemoMode;

    public Task<DomainUsageSnapshot> GetUsageAsync(CancellationToken ct = default)
    {
        // Belt-and-braces: the repository only calls available providers, but self-gate anyway so a
        // future caller can't get demo data while demo mode is off.
        if (!UsageSettingsManager.Instance.DemoMode)
            return Task.FromResult(DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.NotConfigured));

        var now = DateTimeOffset.Now;
        var snapshot = new DomainUsageSnapshot(
            Id,
            DisplayName,
            UsageStatus.Ok,
            [
                new DomainUsageWindow("five_hour", UsageWindowKind.Session, 23, now.AddHours(2).AddMinutes(5)),
                new DomainUsageWindow("seven_day", UsageWindowKind.Week, 41, NextWeekday(now, DayOfWeek.Thursday, hour: 9)),
                new DomainUsageWindow("seven_day_opus", UsageWindowKind.ModelWeek, 67, NextWeekday(now, DayOfWeek.Thursday, hour: 9), Qualifier: "Opus"),
                new DomainUsageWindow("seven_day_sonnet", UsageWindowKind.ModelWeek, 12, NextWeekday(now, DayOfWeek.Thursday, hour: 9), Qualifier: "Sonnet"),
                new DomainUsageWindow("extra_usage", UsageWindowKind.ExtraUsage, 12, ResetsAt: null, Used: 6.00m, Limit: 50.00m),
            ],
            PlanLabel: "Demo",
            FetchedAt: now);
        return Task.FromResult(snapshot);
    }

    // The next occurrence of the given weekday at the given local hour, always in the future (a week
    // out when today IS that weekday past that hour).
    private static DateTimeOffset NextWeekday(DateTimeOffset from, DayOfWeek day, int hour)
    {
        var days = ((int)day - (int)from.DayOfWeek + 7) % 7;
        var candidate = new DateTimeOffset(from.Date.AddDays(days).AddHours(hour), from.Offset);
        return candidate > from ? candidate : candidate.AddDays(7);
    }
}
