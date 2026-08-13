using System;
using System.Globalization;
using System.IO;
using AgentsPanelExtension.Properties;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AgentsPanelExtension;

// Extension settings, surfaced in Command Palette's Settings UI and persisted to
// agentspanel.settings.json under the CmdPal settings folder. Singleton, modeled on
// MarketExtension's MarketSettingsManager. Wired into the host via AgentsPanelCommandsProvider
// (Settings = UsageSettingsManager.Instance.Settings).
//
// ⚠️ Toolkit quirk: TextSetting renders **Description** as the field's on-screen label (it maps Label →
// the Adaptive Card `title`, which Input.Text ignores). So the user-visible text must go in Description;
// Label is kept only as the semantic name.
internal sealed class UsageSettingsManager : JsonSettingsManager
{
    public static readonly UsageSettingsManager Instance = new();

    // Default auto-refresh cadence when the field is blank/unparseable.
    private const int DefaultRefreshMinutes = 5;

    // The FLOOR for the refresh cadence. The Claude usage endpoint rate-limits aggressively (the
    // community-established safe polling floor is ~3 minutes), so any positive value below this is
    // clamped UP in the getter — not just hinted at in the placeholder. 0 still means "off".
    private const int MinRefreshMinutes = 3;

    private readonly TextSetting _refreshMinutes = new(
        "refreshMinutes", DefaultRefreshMinutes.ToString(CultureInfo.InvariantCulture))
    {
        Label = Resources.Settings_Refresh_Label,
        Description = Resources.Settings_Refresh_Desc,
        Placeholder = DefaultRefreshMinutes.ToString(CultureInfo.InvariantCulture),
    };

    // Show the per-model weekly windows (Opus / Sonnet) as their own rows and dock buttons. Off by
    // default — most users care about the overall session + weekly numbers.
    private readonly ToggleSetting _showModelWindows = new("showModelWindows", false)
    {
        Label = Resources.Settings_ShowModelWindows_Label,
        Description = Resources.Settings_ShowModelWindows_Desc,
    };

    // Show the extra-usage (pay-as-you-go credits) row when the account has it enabled. On by default —
    // if you're paying for extra usage you probably want to see it burn down.
    private readonly ToggleSetting _showExtraUsage = new("showExtraUsage", true)
    {
        Label = Resources.Settings_ShowExtraUsage_Label,
        Description = Resources.Settings_ShowExtraUsage_Desc,
    };

    // Auto-refresh cadence in minutes; 0 means off. Bad/negative input falls back to the default, and a
    // positive value below the floor is clamped up to it (see MinRefreshMinutes) — enforced HERE so no
    // caller can accidentally poll the endpoint harder than the floor allows.
    public int RefreshMinutes =>
        int.TryParse(_refreshMinutes.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= 0
            ? (v == 0 ? 0 : Math.Max(v, MinRefreshMinutes))
            : DefaultRefreshMinutes;

    // Whether the poll loop should run at all (false when the user picked 0 = off).
    public bool AutoRefreshEnabled => RefreshMinutes > 0;

    // The cadence as a TimeSpan for PollTicker.
    public TimeSpan RefreshInterval => TimeSpan.FromMinutes(RefreshMinutes);

    // Whether the per-model weekly windows (Opus/Sonnet) render as rows/dock buttons. Read pull-style
    // each render.
    public bool ShowModelWindows => _showModelWindows.Value;

    // Whether the extra-usage credits window renders. Read pull-style each render.
    public bool ShowExtraUsage => _showExtraUsage.Value;

    private UsageSettingsManager()
    {
        FilePath = Path.Combine(Utilities.BaseSettingsPath("Microsoft.CmdPal"), "agentspanel.settings.json");
        Settings.Add(_refreshMinutes);
        Settings.Add(_showModelWindows);
        Settings.Add(_showExtraUsage);
        LoadSettings();
        Settings.SettingsChanged += (_, _) => SaveSettings();
    }
}
