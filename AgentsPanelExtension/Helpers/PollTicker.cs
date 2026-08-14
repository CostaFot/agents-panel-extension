using System;
using System.Reactive.Linq;

namespace AgentsPanelExtension;

// The usage poll ticker — emits a tick WHILE at least one subscriber is active and stops when the last
// one unsubscribes. Cadence is read live from UsageSettingsManager (RefreshInterval /
// AutoRefreshEnabled). Copied from MarketExtension's PollTicker, reduced to the single ticker this
// extension needs.
//
// Pure Rx:
//   * Observable.Generate drives a self-rescheduling timer whose per-step delay is re-read from
//     UsageSettingsManager each iteration, so an interval / on-off change applies without a reload. When
//     auto-refresh is off it idles on a short re-check and the tick is filtered out (Where), so toggling
//     it back on later resumes on the next iteration.
//   * Publish().RefCount() is the WhileSubscribed seam: the Generate loop starts on the first subscriber
//     (0 -> 1) and is torn down on the last unsubscribe (1 -> 0). Generate's condition is always true so
//     the source never completes — disposal is silent (no OnCompleted/OnError reaches the shared
//     subject), so a later resubscribe cleanly restarts the loop.
//
// Subscribe via Subscribe(onTick): the handler is guarded so a synchronous throw can't escape into the
// shared stream. This matters because the published stream is multicast — an unguarded throw would be
// caught by Rx's SafeObserver, which DISPOSES that subscription AND rethrows back through the Publish
// subject, terminating the stream for EVERY subscriber (polling dead until reload).
//
// ⚠️ That guard covers the SUBSCRIBER side only. The SOURCE operators below (timeSelector, Where, Do)
// are NOT guarded: a throw in any of them OnErrors the Publish subject, poisoning it for every
// subscriber. They are safe today because they only do non-throwing UsageSettingsManager property reads
// (parse-with-fallback); keep them that way.
internal static class PollTicker
{
    // How long to idle before re-checking settings while auto-refresh is off, so the user can turn it
    // on mid-view and have polling resume without reloading the extension.
    private static readonly TimeSpan OffRecheckInterval = TimeSpan.FromSeconds(30);

    // The usage ticker. Shared by every surface, so the timer lives while ANY of them is active and goes
    // quiet only when they all stop (a pinned dock keeps it warm). Private so every subscription goes
    // through the guarded Subscribe below.
    private static readonly IObservable<long> Ticks =
        Observable.Defer(() =>
            {
                Log.Info("Poll", "Poll loop started — a subscriber became active");
                return Observable
                    .Generate(
                        initialState: 0L,
                        condition: _ => true,
                        iterate: tick => tick + 1,
                        resultSelector: tick => tick,
                        timeSelector: _ =>
                        {
                            // Re-read live each iteration so an interval/on-off change applies without a reload.
                            var settings = UsageSettingsManager.Instance;
                            return settings.AutoRefreshEnabled ? settings.RefreshInterval : OffRecheckInterval;
                        })
                    .Where(_ => UsageSettingsManager.Instance.AutoRefreshEnabled)
                    .Do(tick => Log.Info("Poll", $"Tick #{tick} — signalling active subscribers to refresh"));
            })
            .Finally(() => Log.Info("Poll", "Poll loop stopped — all subscribers inactive"))
            .Publish()
            .RefCount();

    // Run onTick on every poll tick while subscribed. The handler is wrapped so a throw is contained
    // (swallowed + logged) and can't tear down the shared stream — see the type comment above.
    public static IDisposable Subscribe(Action onTick)
    {
        ArgumentNullException.ThrowIfNull(onTick);
        return Ticks.Subscribe(_ =>
        {
            try { onTick(); }
            catch (Exception ex) { Log.Error("Poll", "tick handler threw — continuing poll loop", ex); }
        });
    }
}
