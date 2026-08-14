using System.Linq;
using AgentsPanelExtension.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AgentsPanelExtension;

public partial class AgentsPanelCommandsProvider : CommandProvider
{
    // The registration array is the extension seam: add a provider here (Copilot, ...) — implement
    // IAgentUsageProvider, add a ProviderIcons case — and it shows up everywhere, including its own
    // dock band.
    private static readonly IAgentUsageProvider[] Providers =
        [new ClaudeUsageProvider(),
         new CodexUsageProvider(),
         new CopilotUsageProvider()];

    // The repository coordinates all agent-usage providers; the palette page and the dock bands share
    // this one instance (single source of truth — all surfaces observe the same flow).
    private readonly UsageRepository _repository = new(Providers);

    // One page per provider, shared by the hub and the dock so both navigate into the same instances.
    private readonly UsageProviderPageCache _providerPages;

    private readonly ICommandItem[] _commands;
    private readonly ICommandItem[] _dockBands;

    public AgentsPanelCommandsProvider()
    {
        Id = "com.costafotiadis.agentspanel";
        DisplayName = Resources.Extension_DisplayName;
        Icon = IconHelpers.FromRelativePath("Assets\\agentspanel_logo_base_square.png");

        // Surface the extension's settings (refresh interval, window toggles) in the Command Palette
        // Settings UI. See Settings/UsageSettingsManager.cs.
        Settings = UsageSettingsManager.Instance.Settings;

        _providerPages = new UsageProviderPageCache(_repository);

        // A single top-level "Agents Panel" command that opens the provider-list hub, keeping the
        // Command Palette root to one entry. The page is constructed once and reused so its held
        // state survives navigating in and out.
        _commands = [
            new CommandItem(new UsagePage(_repository, _providerPages)) { Title = Resources.Command_AgentsPanel },
        ];

        // The dock bands — the extension's main selling point: pinnable quick-look usage buttons
        // ("5h 23%" / "Wk 41%") that live-update while pinned and click through to their provider's
        // page. ONE band per provider, so the user pins/unpins providers individually via the host's
        // own band management (the reason there's no "show X in dock" setting).
        //
        // Threading note (inherited from MarketExtension, where this crashed CmdPal): a band's
        // repository subscription must never deliver synchronously under an Rx lock, because
        // RaiseItemsChanged's blocking COM call re-enters the host's STA and the lock order cycles →
        // hang. UsageRepository.ObserveUsage ends in ObserveOn(TaskPoolScheduler) for exactly this
        // reason — surfaces are notified only after the locks release. Do not remove that hop.
        _dockBands = [.. Providers.Select(p =>
            new CommandItem(new UsageDockPage(_repository, _providerPages, p.Id, p.DisplayName))
            {
                Title = p.DisplayName,
            })];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands() => _dockBands;
}
