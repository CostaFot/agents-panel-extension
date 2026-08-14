using System;
using System.Collections.Generic;
using AgentsPanelExtension.Properties;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Foundation;

namespace AgentsPanelExtension;

// One provider's drill-in page — opened from the hub's provider row or a dock button. One row per
// quota window (title = window name, colored percent pill, subtitle = reset time), then the status
// row, plan row, and Refresh. One instance per provider, cached in UsageProviderPageCache so the hub
// and the dock navigate into the SAME page and its held state survives navigating in and out.
//
// A PURE OBSERVER of the repository's snapshot state: while open it subscribes to
// ObserveUsage(providerId) and renders whatever it emits — it does NOT fetch or poll itself, so it can
// never drift from the hub or dock observing the same flow. Subscribing registers it as an observer
// (waking the repository's poll handler and triggering a refresh when the held state is stale).
//
// Threading: ObserveUsage delivers via ObserveOn (see UsageRepository) so OnUsageChanged — and
// therefore RaiseItemsChanged — runs on a pool thread with NO Rx lock held. Do not add Task.Run here.
internal sealed partial class UsageProviderPage : ListPage, INotifyItemsChanged
{
    private const string SessionGlyph = "\uE823";  // Segoe MDL2 Recent (clock)
    private const string WeekGlyph = "\uE787";     // Segoe MDL2 Calendar
    private const string ExtraGlyph = "\uE8C7";    // Segoe MDL2 Payment

    private readonly UsageRepository _repository;
    private readonly string _providerId;
    private readonly RefreshUsageCommand _refreshCommand;
    private UiUsage? _usage; // latest emission for THIS provider; null before the first / while absent

    private event TypedEventHandler<object, IItemsChangedEventArgs>? _itemsChanged;

    // Subscriptions held in a list (not a single field) so a double-`add` without an intervening
    // `remove` can't orphan a subscription — an orphan would pin this page as a repository observer
    // forever, keeping the poll fetching for a closed page. Dispose-all-and-clear in `remove` keeps
    // every subscribe balanced. Same rationale as UsagePage.
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
            _subscriptions.Add(_repository.ObserveUsage(_providerId).Subscribe(OnUsageChanged));
            Log.Info("ProviderPage", $"observing usage for '{_providerId}'");
        }
        remove
        {
            _itemsChanged -= value;
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
            _subscriptions.Clear();
            Log.Info("ProviderPage", $"stopped observing usage for '{_providerId}'");
        }
    }

    private new void RaiseItemsChanged(int totalItems = -1)
        => _itemsChanged?.Invoke(this, new ItemsChangedEventArgs(totalItems));

    public UsageProviderPage(UsageRepository repository, string providerId, string displayName)
    {
        _repository = repository;
        _providerId = providerId;
        _refreshCommand = new RefreshUsageCommand(repository);
        Id = $"com.costafotiadis.agentspanel.provider.{providerId}"; // non-empty — dock buttons navigate here
        Title = displayName;
        Icon = ProviderIcons.For(providerId);
        IsLoading = true; // until the first emission lands
    }

    public override IListItem[] GetItems()
    {
        var usage = _usage;
        if (usage is null)
            return []; // before the first emission / provider absent — the spinner covers this

        var settings = UsageSettingsManager.Instance;
        var now = DateTimeOffset.UtcNow;
        var items = new List<IListItem>();
        var stale = usage.StaleText();

        foreach (var window in usage.VisibleWindows(settings))
        {
            // When the numbers are stale (keep-last-good), say so instead of a reset time that
            // may already be in the past.
            items.Add(new ListItem(new NoOpCommand { Id = $"{Id}.{window.Window.Id}" })
            {
                Title = window.LongLabel,
                Subtitle = stale ?? window.FormatReset(now),
                Icon = new IconInfo(WindowGlyph(window.Window.Kind)),
                Tags = [new Tag(window.FormatPercent()) { Foreground = window.SeverityColor() }],
            });
        }

        // Ok-but-empty: the account reported no active limits at all — say so rather than
        // rendering a blank page.
        if (usage.Snapshot.Status == UsageStatus.Ok && usage.Windows.Count == 0)
        {
            items.Add(new ListItem(new NoOpCommand { Id = $"{Id}.empty" })
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
            items.Add(new ListItem(new NoOpCommand { Id = $"{Id}.plan" })
            {
                Title = Strings.Format(Resources.Plan_Title, plan),
                Icon = new IconInfo("\uE77B"), // Segoe MDL2 Contact
            });
        }

        items.Add(new ListItem(_refreshCommand) { Title = Resources.Action_Refresh });

        return [.. items];
    }

    private static string WindowGlyph(UsageWindowKind kind) => kind switch
    {
        UsageWindowKind.Session => SessionGlyph,
        UsageWindowKind.ExtraUsage => ExtraGlyph,
        _ => WeekGlyph,
    };

    // A new state emission: project for rendering and repaint. Runs on a pool thread (ObserveOn) — no
    // Rx lock is held here, so RaiseItemsChanged's host call is safe. Null = the provider is absent
    // from the current list (loading / IsAvailable off) — show the spinner until it's back.
    private void OnUsageChanged(DomainUsageSnapshot? snapshot)
    {
        _usage = snapshot is null ? null : UiUsage.From(snapshot);
        IsLoading = _usage is null;
        Log.Info("ProviderPage", $"'{_providerId}' painted: {(snapshot is null ? "absent" : snapshot.Status.ToString())}");
        RaiseItemsChanged(0);
    }
}
