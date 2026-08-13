using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace AgentsPanelExtension;

// A minimal StateFlow analog (Kotlin's StateFlow): holds a current value, REPLAYS it to every new
// subscriber the moment they subscribe, then pushes the new value on each change. Deduplicates with an
// equality comparer (distinct-until-changed).
//
// A thin wrapper over System.Reactive's BehaviorSubject<T>, which already provides the current value,
// replay-on-subscribe, fan-out, and thread-safe, idempotent subscriptions. Copied from MarketExtension
// (same author) where the pattern is battle-tested against CmdPal's threading quirks.
//
// This is the read-only face consumers see (read Value / Subscribe). Only the writable MutableStateFlow
// subclass can change the value — mirroring Kotlin's MutableStateFlow/StateFlow split, so the owner
// (UsageRepository, UsageSettingsManager) mutates while pages and the dock merely observe.
[SuppressMessage("Reliability", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The BehaviorSubject is owned by process-lifetime singletons (UsageRepository, " +
                    "UsageSettingsManager) whose flows are observed for the life of the process; it is " +
                    "intentionally never completed or disposed.")]
internal class StateFlow<T>
{
    private readonly BehaviorSubject<T> _subject;
    private readonly IEqualityComparer<T> _comparer;

    protected StateFlow(T initial, IEqualityComparer<T>? comparer = null)
    {
        _subject = new BehaviorSubject<T>(initial);
        _comparer = comparer ?? EqualityComparer<T>.Default;
    }

    public T Value => _subject.Value;

    // Subscribe and immediately receive the current value (StateFlow replay — BehaviorSubject does this).
    // Dispose to unsubscribe; Rx subscriptions are idempotent on dispose. Handlers run on whatever thread
    // mutates the value (BehaviorSubject fans out OUTSIDE its internal lock) — the toolkit marshals the
    // RaiseItemsChanged that handlers ultimately call.
    public IDisposable Subscribe(Action<T> onNext) => Subscribe(onNext, replayOnSubscribe: true);

    // As above, but pass replayOnSubscribe:false to suppress the initial replay and only receive *future*
    // values (Skip(1) drops the value BehaviorSubject replays synchronously on subscribe). Used where a
    // surface's initial paint is already driven by another flow's replay, so replaying here would just
    // duplicate work.
    public IDisposable Subscribe(Action<T> onNext, bool replayOnSubscribe)
    {
        ArgumentNullException.ThrowIfNull(onNext);

        // Guard the handler so a throw can never escape into Rx. Two reasons:
        //  1. Symmetry: the replayOnSubscribe:false path runs through Skip(1) — an Rx operator whose
        //     SafeObserver DISPOSES the subscription if OnNext throws (silently unsubscribing the surface),
        //     while the raw-BehaviorSubject replay path does not. Guarding makes both paths behave the same
        //     (throw is non-fatal to the subscription).
        //  2. Isolation: BehaviorSubject fans out with a foreach, so one subscriber throwing would skip the
        //     rest for that emission. Swallowing here keeps subscribers independent.
        void Guarded(T value)
        {
            try { onNext(value); }
            catch (Exception ex) { Log.Error("StateFlow", "subscriber handler threw", ex); }
        }

        IObservable<T> source = replayOnSubscribe ? _subject : _subject.Skip(1);
        return source.Subscribe(Guarded);
    }

    // The underlying stream, for Rx composition (UsageRepository.ObserveUsage). Replays the current value
    // on subscribe, like Subscribe(). Bypasses the Guarded wrapper deliberately — composition operators
    // manage their own errors, and these flows never OnError.
    public IObservable<T> AsObservable() => _subject.AsObservable();

    // Writable entry point for subclasses. Returns true if the value actually changed (i.e. listeners
    // were notified). Distinct-until-changed at the source: an equal value is not pushed, so re-publishing
    // an unchanged snapshot doesn't wake its subscribers. OnNext invokes handlers OUTSIDE the subject's
    // lock — handlers re-read the owner, which re-takes its lock, with no lock held here.
    protected bool SetValue(T value)
    {
        if (_comparer.Equals(_subject.Value, value)) return false; // distinct-until-changed
        _subject.OnNext(value);
        return true;
    }
}

// The writable side of a StateFlow — only the data owner (e.g. UsageRepository) holds one of these and
// exposes it typed as the read-only StateFlow<T>.
internal sealed class MutableStateFlow<T>(T initial, IEqualityComparer<T>? comparer = null)
    : StateFlow<T>(initial, comparer)
{
    public bool Update(T value) => SetValue(value);
}

// Compares two snapshot lists by content so a poll that fetched IDENTICAL numbers doesn't wake every
// subscriber (dock + pages) for a no-op repaint. FetchedAt is deliberately EXCLUDED: a successful
// re-fetch of unchanged data bumps only FetchedAt, and the UI shows FetchedAt only in the stale state —
// where it is carried over from the previous snapshot anyway (keep-last-good), so skipping the emission
// never hides a visible change. DomainUsageWindow is a record of scalars, so SequenceEqual is deep.
internal sealed class SnapshotListComparer : IEqualityComparer<IReadOnlyList<DomainUsageSnapshot>>
{
    public static readonly SnapshotListComparer Instance = new();

    private SnapshotListComparer() { }

    public bool Equals(IReadOnlyList<DomainUsageSnapshot>? x, IReadOnlyList<DomainUsageSnapshot>? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null || x.Count != y.Count) return false;

        for (var i = 0; i < x.Count; i++)
        {
            var a = x[i];
            var b = y[i];
            if (!string.Equals(a.ProviderId, b.ProviderId, StringComparison.Ordinal)
                || a.Status != b.Status
                || !string.Equals(a.PlanLabel, b.PlanLabel, StringComparison.Ordinal)
                || !a.Windows.SequenceEqual(b.Windows))
            {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode(IReadOnlyList<DomainUsageSnapshot> obj)
    {
        var hash = new HashCode();
        foreach (var snapshot in obj)
        {
            hash.Add(snapshot.ProviderId, StringComparer.Ordinal);
            hash.Add(snapshot.Status);
        }

        return hash.ToHashCode();
    }
}
