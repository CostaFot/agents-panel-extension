using AgentsPanelExtension.Properties;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AgentsPanelExtension;

// The hub's "Refresh now" row: kicks the repository (which single-flights, so mashing Enter can't stack
// fetches) and keeps the palette open — the rows repaint through the observable when the fetch lands.
internal sealed partial class RefreshUsageCommand : InvokableCommand
{
    private readonly UsageRepository _repository;

    public RefreshUsageCommand(UsageRepository repository)
    {
        _repository = repository;
        Id = "com.costafotiadis.agentspanel.refresh";
        Name = Resources.Action_Refresh;
        Icon = new IconInfo("\uE72C"); // Segoe MDL2 Refresh
    }

    public override CommandResult Invoke()
    {
        _repository.RefreshNow();
        return CommandResult.KeepOpen();
    }
}
