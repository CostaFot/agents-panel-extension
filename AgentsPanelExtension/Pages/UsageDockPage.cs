using System;
using System.Collections.Generic;
using System.Linq;
using AgentsPanelExtension.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Foundation;

namespace AgentsPanelExtension;

// Backs the Command Palette Dock band — the extension's main selling point: a quick-look strip of
// usage buttons (e.g. "5h 23%" / "Wk 41%"); clicking one opens that provider's page (the same cached
// UsageProviderPage instance the hub's provider rows navigate to).
// Returned from AgentsPanelCommandsProvider.GetDockBands() wrapped in a CommandItem.
//
// Because the band's command is an IListPage, the host renders each item from GetItems() as its own
// button within the one band. Button titles must stay within the host's ~100 DIP budget (~15 chars) —
// longer titles get ellipsized, not scrolled — which is why UiUsageWindow.DockTitle() is so terse.
//
// A PURE OBSERVER of the shared usage state: while visible it subscribes to the repository's flow
// (UsageRepository.ObserveUsage) and renders whatever it emits — nothing else. It does NOT fetch, poll,
// or handle demo-mode flips itself: the repository owns all of that, so this band can never drift out
// of sync with the hub page observing the same flow. Subscribing also counts the band as an observer,
// which is what lets the repository poll while the dock is pinned; disposing on hide un-counts it.
//
// Threading: ObserveUsage delivers via ObserveOn (see UsageRepository) so OnUsageChanged — and
// therefore RaiseItemsChanged — runs on a pool thread with NO Rx lock held. That is what makes this
// safe; an earlier MarketExtension revision without the hop deadlocked CmdPal (STA/gate lock cycle).
internal sealed partial class UsageDockPage : ListPage, INotifyItemsChanged
{
    private readonly UsageRepository _repository;
    private readonly UsageProviderPageCache _providerPages; // click → that provider's page
    private UiUsage[]? _usages; // latest emission, projected for rendering; null before the first

    private event TypedEventHandler<object, IItemsChangedEventArgs>? _itemsChanged;

    // Subscriptions held in a list (not a single field) so a double-`add` without an intervening
    // `remove` can't orphan a subscription: a single field would be OVERWRITTEN by the second add,
    // losing the first's reference so it's never disposed — and that orphan would keep this band
    // counted as a repository observer forever, keeping the poll loop fetching for a hidden band.
    // Dispose-all-and-clear in `remove` keeps every subscribe balanced.
    private readonly List<IDisposable> _subscriptions = [];

    // The host subscribes ItemsChanged when the band becomes visible and unsubscribes when it's
    // hidden, so these accessors are the de-facto Loaded/Unloaded hooks. The first emission lands
    // after `add` returns (ObserveOn) — never a synchronous RaiseItemsChanged inside the host's
    // subscription.
    event TypedEventHandler<object, IItemsChangedEventArgs> INotifyItemsChanged.ItemsChanged
    {
        add
        {
            _itemsChanged += value;
            _subscriptions.Add(_repository.ObserveUsage().Subscribe(OnUsageChanged));
            Log.Info("Dock", "band visible — observing usage");
        }
        remove
        {
            _itemsChanged -= value;
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
            _subscriptions.Clear();
            Log.Info("Dock", "band hidden — stopped observing usage");
        }
    }

    private new void RaiseItemsChanged(int totalItems = -1)
        => _itemsChanged?.Invoke(this, new ItemsChangedEventArgs(totalItems));

    public UsageDockPage(UsageRepository repository, UsageProviderPageCache providerPages)
    {
        _repository = repository;
        _providerPages = providerPages;
        Id = "com.costafotiadis.agentspanel.dock.usage"; // dock bands require a non-empty command Id
        Title = Resources.Command_AgentsPanel;
        Icon = IconHelpers.FromRelativePath("Assets\\agentspanel_logo_base_square.png");
    }

    public override IListItem[] GetItems()
    {
        var usages = _usages;
        if (usages is null || usages.Length == 0)
            return []; // before the first emission / mid demo-flip — the spinner covers this

        var settings = UsageSettingsManager.Instance;
        var now = DateTimeOffset.UtcNow;
        var items = new List<IListItem>();

        foreach (var usage in usages)
        {
            var stale = usage.StaleText();

            // Every button for this provider navigates into its (cached) page — the hub's rows link
            // to the same instance, so held state survives either entry path.
            var page = _providerPages.GetPage(usage.Snapshot);

            foreach (var window in usage.VisibleWindows(settings))
            {
                // Stale numbers get a terse "!" marker (the title budget has no room for words);
                // the subtitle carries the explanation.
                items.Add(new ListItem(page)
                {
                    Title = stale is null ? window.DockTitle() : $"{window.DockTitle()} !",
                    Subtitle = stale ?? window.FormatReset(now),
                });
            }

            // No windows to show — the button IS the status (sign in / token expired / no data).
            if (usage.Windows.Count == 0)
            {
                var (title, subtitle) = usage.Snapshot.Status switch
                {
                    UsageStatus.NotConfigured => (Resources.Dock_SignIn_Title,
                        Strings.Format(Resources.Dock_SignIn_Subtitle, usage.Snapshot.ProviderDisplayName)),
                    UsageStatus.TokenExpired => (Resources.Dock_Expired_Title,
                        Strings.Format(Resources.Dock_Expired_Subtitle, usage.Snapshot.ProviderDisplayName)),
                    _ => (Resources.Usage_Empty_Title,
                        Strings.Format(Resources.Status_Error_Title, usage.Snapshot.ProviderDisplayName)),
                };
                items.Add(new ListItem(page) { Title = title, Subtitle = subtitle });
            }
        }

        return [.. items];
    }

    // A new state emission: project for rendering and repaint. Runs on a pool thread (ObserveOn) — no
    // Rx lock is held here, so RaiseItemsChanged's host call is safe.
    private void OnUsageChanged(IReadOnlyList<DomainUsageSnapshot> snapshots)
    {
        _usages = [.. snapshots.Select(UiUsage.From)];
        // The empty list is the repository's "loading" state (first run / demo flip in progress).
        IsLoading = _usages.Length == 0;
        Log.Info("Dock", $"band painted: {_usages.Length} snapshot(s)");
        RaiseItemsChanged(0);
    }
}
