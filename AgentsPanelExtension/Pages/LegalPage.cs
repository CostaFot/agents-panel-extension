using AgentsPanelExtension.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AgentsPanelExtension;

// A plain informational screen making the app's position explicit: independent third-party tool, no
// affiliation with any AI provider, no backend/tracking, credentials stay local and go only to their
// own provider, no guarantees. Reached from the hub. Pure static Markdown — no data fetch, no
// providers, no lifecycle. (Modeled on MarketExtension's DataSourcesPage.)
internal sealed partial class LegalPage : ContentPage
{
    private readonly MarkdownContent _content = new(Resources.Legal_Markdown);

    public LegalPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\agentspanel_logo_base_square.png");
        Title = Resources.Page_Legal_Title;
        Name = Resources.Action_Open;
    }

    public override IContent[] GetContent() => [_content];
}
