using System;
using System.Collections.Generic;
using System.Linq;
using AgentsPanelExtension.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Foundation;

namespace AgentsPanelExtension;

// The hub page — opened by the single top-level "Agents Panel" command. One row per PROVIDER
// (title = provider name, subtitle = compact summary like "5h 23% · Wk 41%", worst-window percent
// pill), each navigating into that provider's UsageProviderPage, then Refresh / Settings. The
// per-window detail lives on the provider pages — with more providers than Claude on the way, the
// hub can't be a flat dump of every window of every provider.
//
// A PURE OBSERVER of the repository's snapshot state: while open it subscribes to ObserveUsage() and
// renders whatever it emits — it does NOT fetch or poll itself, so it can never drift from the dock
// band observing the same flow. Subscribing registers it as an observer (waking the repository's poll
// handler and triggering a refresh when the held state is stale).
//
// Threading: ObserveUsage delivers via ObserveOn (see UsageRepository) so OnUsageChanged — and
// therefore RaiseItemsChanged — runs on a pool thread with NO Rx lock held. Do not add Task.Run here.
internal sealed partial class UsagePage : ListPage, INotifyItemsChanged
{
    private const string SettingsGlyph = "\uE713"; // Segoe MDL2 Settings

    private readonly UsageRepository _repository;
    private readonly UsageProviderPageCache _providerPages;
    private readonly RefreshUsageCommand _refreshCommand;
    private UiUsage[]? _snapshots; // latest emission, projected for rendering; null before the first

    private event TypedEventHandler<object, IItemsChangedEventArgs>? _itemsChanged;

    // Subscriptions held in a list (not a single field) so a double-`add` without an intervening
    // `remove` can't orphan a subscription — an orphan would pin this page as a repository observer
    // forever, keeping the poll fetching for a closed page. Dispose-all-and-clear in `remove` keeps
    // every subscribe balanced. Same rationale as MarketExtension's FavoritesDockPage.
    private readonly List<IDisposable> _subscriptions = [];

    // The page-activation hook: GetItems() is called BEFORE the host subscribes ItemsChanged, so a
    // constructor RaiseItemsChanged would be lost. Instead the repository subscription made here in
    // `add` replays the current state off-thread (ObserveOn), landing after this accessor returns —
    // the first paint comes through OnUsageChanged, never synchronously inside the host's subscribe.
    event TypedEventHandler<object, IItemsChangedEventArgs> INotifyItemsChanged.ItemsChanged
    {
        add
        {
            _itemsChanged += value;
            _subscriptions.Add(_repository.ObserveUsage().Subscribe(OnUsageChanged));
            Log.Info("UsagePage", "observing usage");
        }
        remove
        {
            _itemsChanged -= value;
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
            _subscriptions.Clear();
            Log.Info("UsagePage", "stopped observing usage");
        }
    }

    private new void RaiseItemsChanged(int totalItems = -1)
        => _itemsChanged?.Invoke(this, new ItemsChangedEventArgs(totalItems));

    public UsagePage(UsageRepository repository, UsageProviderPageCache providerPages)
    {
        _repository = repository;
        _providerPages = providerPages;
        _refreshCommand = new RefreshUsageCommand(repository);
        Id = "com.costafotiadis.agentspanel.usage";
        Title = Resources.Page_Usage_Title;
        Icon = IconHelpers.FromRelativePath("Assets\\agentspanel_logo_base_square.png");
        IsLoading = true; // until the first emission lands
    }

    public override IListItem[] GetItems()
    {
        var snapshots = _snapshots;
        if (snapshots is null)
            return []; // before the first emission — the spinner covers this

        var settings = UsageSettingsManager.Instance;
        var items = new List<IListItem>();

        foreach (var usage in snapshots)
        {
            // Enter navigates into the provider's own page (same cached instance the dock links to).
            var page = _providerPages.GetPage(usage.Snapshot);

            // Worst-window percent pill first (the number you'd act on), then the status pill when
            // the snapshot is degraded — both may be present with keep-last-good stale numbers.
            var tags = new List<Tag>();
            if (usage.WorstVisibleWindow(settings) is { } worst)
                tags.Add(new Tag(worst.FormatPercent()) { Foreground = worst.SeverityColor() });
            if (UsageStatusHint.StatusTag(usage.Snapshot.Status) is { } statusTag)
                tags.Add(statusTag);

            // Stale marker wins over the summary; a provider with no visible windows explains itself
            // via its status subtitle ("sign in" / "expired") or the empty-account text.
            var subtitle = usage.StaleText()
                ?? usage.SummaryText(settings)
                ?? UsageStatusHint.StatusSubtitle(usage.Snapshot.Status)
                ?? Resources.Usage_Empty_Title;

            items.Add(new ListItem(page)
            {
                Title = usage.Snapshot.ProviderDisplayName,
                Subtitle = subtitle,
                Tags = [.. tags],
            });
        }

        if (UsageStatusHint.DemoRow() is { } demo)
            items.Add(demo);

        items.Add(new ListItem(_refreshCommand) { Title = Resources.Action_Refresh });
        items.Add(new ListItem(UsageSettingsManager.Instance.Settings.SettingsPage)
        {
            Title = Resources.Action_Settings,
            Icon = new IconInfo(SettingsGlyph),
        });

        return [.. items];
    }

    // A new state emission: project for rendering and repaint. Runs on a pool thread (ObserveOn) — no
    // Rx lock is held here, so RaiseItemsChanged's host call is safe.
    private void OnUsageChanged(IReadOnlyList<DomainUsageSnapshot> snapshots)
    {
        _snapshots = [.. snapshots.Select(UiUsage.From)];
        // The empty list is the repository's "loading" state (cleared on demo flip / first run).
        IsLoading = _snapshots.Length == 0;
        Log.Info("UsagePage", $"usage painted: {_snapshots.Length} snapshot(s)");
        RaiseItemsChanged(0);
    }
}