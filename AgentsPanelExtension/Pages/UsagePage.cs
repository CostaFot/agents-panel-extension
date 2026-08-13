using System;
using System.Collections.Generic;
using System.Linq;
using AgentsPanelExtension.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Foundation;

namespace AgentsPanelExtension;

// The hub page — opened by the single top-level "Agents Panel" command. One row per quota window
// (title = window name, colored percent pill, subtitle = reset time), then per-provider status rows,
// then Refresh / Settings.
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
    private const string SessionGlyph = "\uE823";  // Segoe MDL2 Recent (clock)
    private const string WeekGlyph = "\uE787";     // Segoe MDL2 Calendar
    private const string ExtraGlyph = "\uE8C7";    // Segoe MDL2 Payment
    private const string SettingsGlyph = "\uE713"; // Segoe MDL2 Settings

    private readonly UsageRepository _repository;
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

    public UsagePage(UsageRepository repository)
    {
        _repository = repository;
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
        var now = DateTimeOffset.UtcNow;
        var items = new List<IListItem>();

        foreach (var usage in snapshots)
        {
            var stale = usage.StaleText();

            foreach (var window in usage.Windows)
            {
                if (window.Window.Kind == UsageWindowKind.ModelWeek && !settings.ShowModelWindows)
                    continue;
                if (window.Window.Kind == UsageWindowKind.ExtraUsage && !settings.ShowExtraUsage)
                    continue;

                // When the numbers are stale (keep-last-good), say so instead of a reset time that
                // may already be in the past.
                items.Add(new ListItem(new NoOpCommand { Id = $"{Id}.{usage.Snapshot.ProviderId}.{window.Window.Id}" })
                {
                    Title = window.LongLabel,
                    Subtitle = stale ?? window.FormatReset(now),
                    Icon = new IconInfo(WindowGlyph(window.Window.Kind)),
                    Tags = [new Tag(window.FormatPercent()) { Foreground = window.SeverityColor() }],
                });
            }

            // Ok-but-empty: the account reported no active limits at all — say so rather than
            // rendering a blank section.
            if (usage.Snapshot.Status == UsageStatus.Ok && usage.Windows.Count == 0)
            {
                items.Add(new ListItem(new NoOpCommand { Id = $"{Id}.{usage.Snapshot.ProviderId}.empty" })
                {
                    Title = Resources.Usage_Empty_Title,
                    Subtitle = Resources.Usage_Empty_Subtitle,
                });
            }

            // The problem row (sign in / expired / rate-limited / error) — the whole story when there
            // are no windows, a footnote under the stale numbers otherwise.
            if (UsageStatusHint.StatusRow(usage.Snapshot) is { } status)
                items.Add(status);

            // Plan metadata (e.g. "Max 20x"), when the provider knows it.
            if (usage.Snapshot.PlanLabel is { } plan)
            {
                items.Add(new ListItem(new NoOpCommand { Id = $"{Id}.{usage.Snapshot.ProviderId}.plan" })
                {
                    Title = Strings.Format(Resources.Plan_Title, plan),
                    Icon = new IconInfo("\uE77B"), // Segoe MDL2 Contact
                });
            }
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

    private static string WindowGlyph(UsageWindowKind kind) => kind switch
    {
        UsageWindowKind.Session => SessionGlyph,
        UsageWindowKind.ExtraUsage => ExtraGlyph,
        _ => WeekGlyph,
    };

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
