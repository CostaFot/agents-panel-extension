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

    // Show the "Tokens today" row on provider pages — token counts read from the agent CLI's own
    // local session logs (machine-local activity, unrelated to the quota percentages). On by default.
    private readonly ToggleSetting _showTokenStats = new("showTokenStats", true)
    {
        Label = Resources.Settings_ShowTokenStats_Label,
        Description = Resources.Settings_ShowTokenStats_Desc,
    };

    // Also show a token-count button in the provider's Dock band. On by default; subordinate to
    // _showTokenStats — turning token counts off hides the dock button too.
    private readonly ToggleSetting _showTokenStatsInDock = new("showTokenStatsInDock", true)
    {
        Label = Resources.Settings_ShowTokenStatsInDock_Label,
        Description = Resources.Settings_ShowTokenStatsInDock_Desc,
    };

    // Optional explicit GitHub token for the Copilot provider (e.g. a fine-grained PAT with
    // "Copilot Requests: Read"). Blank by default — CopilotCredentialsReader then falls back to the
    // tokens gh CLI / Copilot CLI / the editor plugins already left on the machine. ⚠️ Persisted in
    // plain text in agentspanel.settings.json (same trust level as the credential files it replaces).
    private readonly TextSetting _copilotToken = new("copilotToken", string.Empty)
    {
        Label = Resources.Settings_CopilotToken_Label,
        Description = Resources.Settings_CopilotToken_Desc,
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

    // Whether the local-log token counts row renders. Read pull-style each render.
    public bool ShowTokenStats => _showTokenStats.Value;

    // Whether the dock band also gets a token-count button. Requires ShowTokenStats.
    public bool ShowTokenStatsInDock => _showTokenStats.Value && _showTokenStatsInDock.Value;

    // The user's explicit Copilot token, or null when blank/whitespace. Read pull-style each fetch.
    // NEVER log the value.
    public string? CopilotToken =>
        string.IsNullOrWhiteSpace(_copilotToken.Value) ? null : _copilotToken.Value.Trim();

    private UsageSettingsManager()
    {
        FilePath = Path.Combine(Utilities.BaseSettingsPath("Microsoft.CmdPal"), "agentspanel.settings.json");
        Settings.Add(_refreshMinutes);
        Settings.Add(_showModelWindows);
        Settings.Add(_showExtraUsage);
        Settings.Add(_showTokenStats);
        Settings.Add(_showTokenStatsInDock);
        Settings.Add(_copilotToken);
        LoadSettings();
        Settings.SettingsChanged += (_, _) => SaveSettings();
    }
}
