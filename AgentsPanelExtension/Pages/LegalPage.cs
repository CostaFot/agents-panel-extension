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
    private readonly MarkdownContent _content = new(
        """
        # The Legal Bit

        This app is an **independent, third-party tool**. It is **not
        affiliated with, endorsed by, or connected to** Anthropic, Claude,
        OpenAI, ChatGPT, GitHub, Copilot, or any AI provider. Provider names
        and logos belong to their respective owners.

        ## No backend, no tracking

        This app has **no server, no analytics, and no telemetry**. It
        collects nothing, stores nothing outside your machine, and shares
        nothing with anyone. The only network requests it makes are the usage
        checks you see on screen, sent **directly to the provider**.

        ## Your credentials stay yours

        The app reads the sign-in credentials that a provider's own app
        (e.g. Claude Code) already keeps on your machine. It never creates,
        modifies, uploads, or proxies them — each token is read locally and
        sent **only to the provider it belongs to**, and nowhere else.

        Your account and any agreement governing it are **between you and
        that provider**. This app is just a surface that displays what the
        provider reports, and you are responsible for using your account
        within the provider's terms.

        ## No guarantees

        Usage numbers come from provider endpoints that are **not officially
        documented for third-party use** — they can be delayed, inaccurate,
        or stop working at any time without notice. The app is provided
        **as is, without warranty of any kind**. Don't rely on it as the
        authoritative record of your usage or billing — the provider's own
        app and website are.

        ## What this app does and doesn't do

        **Does:** read your local sign-in, ask the provider for your usage
        numbers, and show them to you.

        **Doesn't:** run a server, track you, phone home, refresh or modify
        your credentials, or send anything anywhere except the provider's
        own API.
        """);

    public LegalPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\agentspanel_logo_base_square.png");
        Title = Resources.Page_Legal_Title;
        Name = Resources.Action_Open;
    }

    public override IContent[] GetContent() => [_content];
}
