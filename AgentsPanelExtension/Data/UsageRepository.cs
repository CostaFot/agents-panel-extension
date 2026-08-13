using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AgentsPanelExtension;

// THE single source of truth for usage data — the coordinator every UI surface depends on. Owns the one
// observable snapshot state and the poll lifecycle; the dock band and the hub page both OBSERVE the same
// flow, so the same quota can never drift between two surfaces. Adapted from MarketExtension's
// MarketRepository, radically simplified: every surface shows the same whole snapshot list, so there is
// no per-key cache, no DynamicData, no routing-by-category — one MutableStateFlow is the cache.
//
// Lifecycle: the ticker subscription is permanent (process lifetime), but the tick handler no-ops while
// no surface is observing (_observerCount == 0), so no credential read / HTTP happens off-screen — the
// Generate timer just runs one cheap no-op callback per interval. Observers are refcounted by
// ObserveUsage's subscribe/dispose hooks; a subscribe when the held state is missing or stale triggers
// an immediate refresh, so a dock band re-shown after an hour repaints instantly from the held snapshot
// and refreshes behind it, while a re-show 30s later costs zero HTTP.
internal sealed class UsageRepository
{
    private const string Tag = "Repository";

    private readonly IAgentUsageProvider[] _providers;

    private readonly MutableStateFlow<IReadOnlyList<DomainUsageSnapshot>> _state =
        new([], SnapshotListComparer.Instance);

    private int _observerCount;      // surfaces currently subscribed (dock band, hub page)
    private int _refreshing;         // Interlocked single-flight gate
    private long _lastRefreshTicks;  // Environment.TickCount64 of the last refresh ATTEMPT; 0 = never

    // Providers in priority order. The registration array is the extension seam: add a provider here
    // (see AgentsPanelCommandsProvider) and it shows up everywhere.
    public UsageRepository(params IAgentUsageProvider[] providers)
    {
        _providers = providers;

        // Permanent ticker subscription — the handler no-ops with no observers (see type comment).
        _ = PollTicker.Subscribe(() =>
        {
            if (Volatile.Read(ref _observerCount) > 0)
                Refresh("poll");
        });

        // Demo flip swaps the entire data source, so every held snapshot is from the OTHER source and
        // now wrong: clear immediately (live streams emit the empty list = loading) and re-fetch if
        // anything is on screen; otherwise the next subscribe sees the empty state and fetches then.
        _ = UsageSettingsManager.Instance.DemoModeChanged.Subscribe(
            _ =>
            {
                Log.Info(Tag, "demo mode flipped — clearing snapshots and re-fetching");
                Volatile.Write(ref _lastRefreshTicks, 0);
                _state.Update([]);
                if (Volatile.Read(ref _observerCount) > 0)
                    Refresh("demo-flip");
            },
            replayOnSubscribe: false);
    }

    // The whole UI contract: subscribe to receive the current snapshot list immediately (replay) and
    // every change after. Subscribing registers the surface as an observer (waking the poll handler) and
    // triggers a refresh when the held state is missing/stale; disposing unregisters it.
    //
    // ⚠️ THE ObserveOn HOP IS LOAD-BEARING. Without it an Update fans out synchronously on the mutating
    // thread, and a surface's RaiseItemsChanged (a blocking COM call into CmdPal's STA) can then run
    // while Rx/producer locks are held → lock-order cycle → the palette hangs. MarketExtension debugged
    // exactly this (see MarketRepository.ObserveQuotes); SubscribeOn does NOT fix it — it moves where
    // you subscribe, not where notifications fire. Do not remove, and don't add your own Task.Run on
    // the delivery path.
    public IObservable<IReadOnlyList<DomainUsageSnapshot>> ObserveUsage() =>
        Observable.Create<IReadOnlyList<DomainUsageSnapshot>>(observer =>
            {
                OnObserverAdded();
                return new CompositeDisposable(
                    _state.AsObservable().Subscribe(observer),
                    Disposable.Create(OnObserverRemoved));
            })
            .ObserveOn(TaskPoolScheduler.Default);

    // User-initiated refresh (the hub's "Refresh now" row): always fetches, observers or not.
    public void RefreshNow() => Refresh("manual");

    private void OnObserverAdded()
    {
        var count = Interlocked.Increment(ref _observerCount);
        Log.Info(Tag, $"observer added (now {count})");

        // The single stale-check (mirrors MarketRepository.NeedsFetchOnSubscribe): never-fetched/empty
        // state always fetches; otherwise only if auto-refresh is on and the held data is at least one
        // interval old. TickCount64 is monotonic — immune to wall-clock changes.
        var settings = UsageSettingsManager.Instance;
        var last = Volatile.Read(ref _lastRefreshTicks);
        var needsFetch = _state.Value.Count == 0
            || (settings.AutoRefreshEnabled
                && Environment.TickCount64 - last >= settings.RefreshInterval.TotalMilliseconds);
        if (needsFetch)
            Refresh("subscribe");
    }

    private void OnObserverRemoved()
    {
        var count = Interlocked.Decrement(ref _observerCount);
        Log.Info(Tag, $"observer removed (now {count})");
    }

    // Fire-and-forget seam: every trigger (poll tick, subscribe, demo flip, manual) funnels here so a
    // provider failure can never become an unobserved task exception.
    private void Refresh(string reason) => _ = RefreshAsync(reason);

    private async Task RefreshAsync(string reason)
    {
        // Single flight: a poll tick landing while a subscribe-triggered fetch is in flight is dropped
        // rather than queued — the in-flight result is at most seconds old.
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            Log.Info(Tag, $"refresh ({reason}) skipped — already in flight");
            return;
        }

        try
        {
            Log.Info(Tag, $"refresh ({reason}) starting");
            Volatile.Write(ref _lastRefreshTicks, Environment.TickCount64);
            var active = ActiveProviders();
            var fresh = await Task.WhenAll(active.Select(FetchSafeAsync)).ConfigureAwait(false);
            _state.Update(Merge(_state.Value, fresh));
        }
        catch (Exception ex)
        {
            // Providers contractually don't throw, so this is belt-and-braces for the merge/update path.
            Log.Error(Tag, $"refresh ({reason}) failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    // The active set: exclusive-wins among the available providers (the mock flips IsExclusive on in
    // demo mode and takes over), else all available providers in registration order. NOTE: unlike
    // MarketExtension there is no configured/keyed filter — an unconfigured real provider stays active
    // and reports NotConfigured (see IAgentUsageProvider).
    private IAgentUsageProvider[] ActiveProviders()
    {
        var available = _providers.Where(p => p.IsAvailable).ToArray();
        var exclusive = available.Where(p => p.IsExclusive).ToArray();
        return exclusive.Length > 0 ? exclusive : available;
    }

    // Contract guard around a provider fetch: GetUsageAsync must not throw, but if one does (a bug),
    // log it loudly and degrade that provider to an Error snapshot instead of killing the whole batch.
    private static async Task<DomainUsageSnapshot> FetchSafeAsync(IAgentUsageProvider provider)
    {
        try
        {
            return await provider.GetUsageAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"provider '{provider.Id}' threw from GetUsageAsync — contract violation", ex);
            return DomainUsageSnapshot.Failed(provider.Id, provider.DisplayName, UsageStatus.Error);
        }
    }

    // Keep-last-good: a failed fresh snapshot (RateLimited / Error / TokenExpired) does NOT blank a
    // previously good one — the old Windows/FetchedAt/PlanLabel survive with the NEW status riding on
    // them, so the UI shows the last real numbers marked stale. A fresh Ok replaces outright. So does
    // NotConfigured: the credentials are GONE, and old numbers with a "sign in" prompt would mislead.
    // Membership is defined by the fresh set — providers that dropped out (demo flip, IsAvailable off)
    // disappear.
    private static IReadOnlyList<DomainUsageSnapshot> Merge(
        IReadOnlyList<DomainUsageSnapshot> prev, DomainUsageSnapshot[] fresh)
    {
        var prevById = prev.ToDictionary(s => s.ProviderId, StringComparer.Ordinal);
        var merged = new List<DomainUsageSnapshot>(fresh.Length);
        foreach (var snapshot in fresh)
        {
            var keepLastGood = snapshot.Status is not (UsageStatus.Ok or UsageStatus.NotConfigured)
                && prevById.TryGetValue(snapshot.ProviderId, out var last)
                && last.Windows.Count > 0;
            merged.Add(keepLastGood
                ? prevById[snapshot.ProviderId] with { Status = snapshot.Status }
                : snapshot);
        }

        return merged;
    }
}
