using System;
using System.Collections.Generic;
using AgentsPanelExtension.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Foundation;

namespace AgentsPanelExtension;

// Backs ONE provider's Command Palette Dock band — the extension's main selling point: a quick-look
// strip of usage buttons (e.g. "5h 23%" / "Wk 41%"); clicking one opens that provider's page (the
// same cached UsageProviderPage instance the hub's provider rows navigate to).
// AgentsPanelCommandsProvider.GetDockBands() returns one instance per registered provider, each
// wrapped in a CommandItem, so the user pins/unpins providers individually via the host's own band
// management — no settings toggle needed.
//
// Because the band's command is an IListPage, the host renders each item from GetItems() as its own
// button within the one band. Button titles must stay within the host's ~100 DIP budget (~15 chars) —
// longer titles get ellipsized, not scrolled — which is why UiUsageWindow.DockTitle() is so terse.
//
// A PURE OBSERVER of the shared usage state: while visible it subscribes to this provider's
// projection of the repository's flow (UsageRepository.ObserveUsage(providerId)) and renders
// whatever it emits — nothing else. It does NOT fetch or poll itself: the repository owns all of
// that, so this band can never drift out of sync with the hub page observing the same flow.
// Subscribing also counts the band as an observer, which is what lets the repository poll while the
// dock is pinned; disposing on hide un-counts it.
//
// Threading: ObserveUsage delivers via ObserveOn (see UsageRepository) so OnUsageChanged — and
// therefore RaiseItemsChanged — runs on a pool thread with NO Rx lock held. That is what makes this
// safe; an earlier MarketExtension revision without the hop deadlocked CmdPal (STA/gate lock cycle).
internal sealed partial class UsageDockPage : ListPage, INotifyItemsChanged
{
    private readonly UsageRepository _repository;
    private readonly UsageProviderPageCache _providerPages; // click → this provider's page
    private readonly string _providerId;
    private UiUsage? _usage; // latest emission, projected for rendering; null before the first

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
            _subscriptions.Add(_repository.ObserveUsage(_providerId).Subscribe(OnUsageChanged));
            Log.Info("Dock", $"{_providerId} band visible — observing usage");
        }
        remove
        {
            _itemsChanged -= value;
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
            _subscriptions.Clear();
            Log.Info("Dock", $"{_providerId} band hidden — stopped observing usage");
        }
    }

    private new void RaiseItemsChanged(int totalItems = -1)
        => _itemsChanged?.Invoke(this, new ItemsChangedEventArgs(totalItems));

    public UsageDockPage(UsageRepository repository, UsageProviderPageCache providerPages,
        string providerId, string displayName)
    {
        _repository = repository;
        _providerPages = providerPages;
        _providerId = providerId;
        // Dock bands require a non-empty command Id — and with one band per provider the Id must be
        // unique per band, or the host conflates them.
        Id = $"com.costafotiadis.agentspanel.dock.{providerId}";
        Title = displayName;
        Icon = ProviderIcons.For(providerId);
    }

    public override IListItem[] GetItems()
    {
        var usage = _usage;
        if (usage is null)
            return []; // before this provider's first emission — the spinner covers this

        var settings = UsageSettingsManager.Instance;
        var now = DateTimeOffset.UtcNow;
        var items = new List<IListItem>();
        var stale = usage.StaleText();

        // Every button navigates into this provider's (cached) page — the hub's rows link to the
        // same instance, so held state survives either entry path.
        var page = _providerPages.GetPage(usage.Snapshot);

        // With multiple bands pinned the terse titles collide ("5h 23%" could be anyone's) — the
        // provider icon is what identifies a button's owner.
        var icon = ProviderIcons.For(usage.Snapshot.ProviderId);

        foreach (var window in usage.VisibleWindows(settings))
        {
            // Stale numbers get a terse "!" marker (the title budget has no room for words);
            // the subtitle carries the explanation.
            items.Add(new ListItem(page)
            {
                Title = stale is null ? window.DockTitle() : $"{window.DockTitle()} !",
                Subtitle = stale ?? window.FormatReset(now),
                Icon = icon,
            });
        }

        // No windows to show — the button IS the status. Ok-but-empty is a healthy account that
        // reported no active limits; it must NOT borrow the error wording (we reached the API fine).
        if (usage.Windows.Count == 0)
        {
            var (title, subtitle) = usage.Snapshot.Status switch
            {
                UsageStatus.NotConfigured => (Resources.Dock_SignIn_Title,
                    Strings.Format(Resources.Dock_SignIn_Subtitle, usage.Snapshot.ProviderDisplayName)),
                UsageStatus.TokenExpired => (Resources.Dock_Expired_Title,
                    Strings.Format(Resources.Dock_Expired_Subtitle, usage.Snapshot.ProviderDisplayName)),
                UsageStatus.RateLimited => (Resources.Dock_RateLimited_Title,
                    Strings.Format(Resources.Status_RateLimited_Title, usage.Snapshot.ProviderDisplayName)),
                UsageStatus.Ok => (Resources.Usage_Empty_Title, Resources.Usage_Empty_Subtitle),
                _ => (Resources.Usage_Empty_Title,
                    Strings.Format(Resources.Status_Error_Title, usage.Snapshot.ProviderDisplayName)),
            };
            items.Add(new ListItem(page) { Title = title, Subtitle = subtitle, Icon = icon });
        }

        return [.. items];
    }

    // A new state emission: project for rendering and repaint. Runs on a pool thread (ObserveOn) — no
    // Rx lock is held here, so RaiseItemsChanged's host call is safe. A null snapshot means this
    // provider hasn't been fetched yet (repository loading) — that's the spinner state.
    private void OnUsageChanged(DomainUsageSnapshot? snapshot)
    {
        _usage = snapshot is null ? null : UiUsage.From(snapshot);
        IsLoading = _usage is null;
        Log.Info("Dock", $"{_providerId} band painted: {(_usage is null ? "loading" : $"{_usage.Windows.Count} window(s)")}");
        RaiseItemsChanged(0);
    }
}
