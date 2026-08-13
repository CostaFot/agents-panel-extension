using AgentsPanelExtension.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AgentsPanelExtension;

public partial class AgentsPanelCommandsProvider : CommandProvider
{
    // The repository coordinates all agent-usage providers; the palette page and the dock band share
    // this one instance (single source of truth — both observe the same flow). MockUsageProvider is
    // FIRST but gated on the Demo-mode setting (IsAvailable/IsExclusive are false unless Demo mode is
    // on), so it takes over everything when demoing and is otherwise skipped. Add a provider here
    // (ChatGPT, Copilot, ...) to extend coverage — implement IAgentUsageProvider and register it.
    private readonly UsageRepository _repository =
        new(new MockUsageProvider(),
            new ClaudeUsageProvider());

    private readonly ICommandItem[] _commands;
    private readonly ICommandItem[] _dockBands;

    public AgentsPanelCommandsProvider()
    {
        Id = "com.costafotiadis.agentspanel";
        DisplayName = Resources.Extension_DisplayName;
        Icon = IconHelpers.FromRelativePath("Assets\\agentspanel_logo_base_square.png");

        // Surface the extension's settings (refresh interval, demo mode, window toggles) in the
        // Command Palette Settings UI. See Settings/UsageSettingsManager.cs.
        Settings = UsageSettingsManager.Instance.Settings;

        // A single top-level "Agents Panel" command that opens the usage hub, keeping the Command
        // Palette root to one entry. The page is constructed once and reused so its held state
        // survives navigating in and out.
        _commands = [
            new CommandItem(new UsagePage(_repository)) { Title = Resources.Command_AgentsPanel },
        ];

        // The dock band — the extension's main selling point: pinnable quick-look usage buttons
        // ("5h 23%" / "Wk 41%") that live-update while pinned and click through to the hub.
        //
        // Threading note (inherited from MarketExtension, where this crashed CmdPal): the band's
        // repository subscription must never deliver synchronously under an Rx lock, because
        // RaiseItemsChanged's blocking COM call re-enters the host's STA and the lock order cycles →
        // hang. UsageRepository.ObserveUsage ends in ObserveOn(TaskPoolScheduler) for exactly this
        // reason — surfaces are notified only after the locks release. Do not remove that hop.
        _dockBands = [
            new CommandItem(new UsageDockPage(_repository)) { Title = Resources.Command_AgentsPanel },
        ];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands() => _dockBands;
}
