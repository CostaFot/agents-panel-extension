using System;
using System.Collections.Generic;

namespace AgentsPanelExtension;

// What kind of quota window a provider reported. Provider-agnostic: Claude maps five_hour → Session,
// seven_day → Week, seven_day_opus/seven_day_sonnet → ModelWeek, extra_usage → ExtraUsage; Codex picks
// Session/Week/Month from each window's duration (its windows vary by plan — e.g. the Go plan has one
// 30-day window). Future providers map their own shapes onto the same kinds so the UI needs no
// per-provider knowledge. ⚠️ Declaration order IS the UI display order (UiUsage sorts by Kind).
internal enum UsageWindowKind
{
    Session,
    Week,
    Month,
    ModelWeek,
    ExtraUsage,
}

// One quota window: utilization 0–100 plus when it resets. NO formatting here — that's UiUsage's job.
// Qualifier disambiguates ModelWeek windows ("Opus", "Sonnet"). Used/Limit are only populated for
// ExtraUsage (credits spent vs monthly cap); null elsewhere.
internal sealed record DomainUsageWindow(
    string Id,                     // provider-scoped stable id, e.g. "five_hour"
    UsageWindowKind Kind,
    double Utilization,            // 0–100 (clamped on ingest)
    DateTimeOffset? ResetsAt,      // null when the window has no reset (ExtraUsage)
    string? Qualifier = null,      // e.g. "Opus" for a per-model weekly window
    decimal? Used = null,          // ExtraUsage: used_credits
    decimal? Limit = null);        // ExtraUsage: monthly_limit

// Why a snapshot has no usable windows — or, after a keep-last-good merge, the degradation flag
// riding on an old-but-good one.
internal enum UsageStatus
{
    Ok,
    NotConfigured,   // no credentials found — the provider can't fetch at all
    TokenExpired,    // credentials found but expired — user must re-auth in the source app
    RateLimited,     // the usage endpoint itself is throttling us
    Error,           // network/HTTP/parse failure
}

// Tokens consumed in the last 24 hours (rolling), summed from the provider CLI's own session logs on
// THIS machine — a different beast from the quota windows: machine-local (other devices/web don't
// appear) and NOT convertible to quota % (limits are opaque weighted units server-side). Null on the
// snapshot when the provider has no local logs (Copilot) or the read failed. NO formatting here.
internal sealed record DomainTokenStats(
    long InputTokens,              // non-cache input
    long OutputTokens,
    long CacheReadTokens,          // cache_read_input_tokens
    long CacheWriteTokens)         // cache_creation_input_tokens
{
    // All four counters summed — the ecosystem's "big number" (it deliberately includes cache
    // replay volume; that's what makes it land at a glance).
    public long TotalTokens => InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens;
}

// One provider's usage state. Providers return this and NEVER throw for expected failures: a failure is
// a snapshot with Status != Ok and empty Windows. The repository then merges it with the last good
// snapshot (keeping Windows/FetchedAt/PlanLabel, taking the new Status) so a bad poll never blanks a
// good reading — the UI shows the old numbers marked stale. See UsageRepository.Merge.
internal sealed record DomainUsageSnapshot(
    string ProviderId,
    string ProviderDisplayName,
    UsageStatus Status,
    IReadOnlyList<DomainUsageWindow> Windows,
    string? PlanLabel,             // e.g. "Max 20x" from subscriptionType + rateLimitTier
    DateTimeOffset? FetchedAt,     // when Windows were last SUCCESSFULLY fetched; null = never
    DomainTokenStats? TokenStats = null) // today's local-log token counts; independent of Status
{
    // True when this snapshot is showing old numbers under a non-Ok status (keep-last-good survivor).
    public bool IsStale => Status != UsageStatus.Ok && Windows.Count > 0;

    public static DomainUsageSnapshot Failed(string id, string name, UsageStatus status) =>
        new(id, name, status, [], PlanLabel: null, FetchedAt: null);
}
