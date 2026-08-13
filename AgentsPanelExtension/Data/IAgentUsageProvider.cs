using System.Threading;
using System.Threading.Tasks;

namespace AgentsPanelExtension;

// One agent/LLM usage source (the Claude OAuth endpoint, the offline mock, a future ChatGPT/Copilot
// source). UsageRepository coordinates these; the UI depends on the repository only. Modeled on
// MarketExtension's IMarketDataProvider, with one deliberate difference: agent providers are NOT
// interchangeable (there is no fallback routing — your Claude usage can only come from Claude), so
// "not signed in" does NOT remove a provider from the active set. It stays visible and reports a
// NotConfigured snapshot, which is what lets the dock render a "Sign in" button for it.
internal interface IAgentUsageProvider
{
    // Stable id, e.g. "claude". Keys the keep-last-good merge and UI identity.
    string Id { get; }

    // User-facing name, e.g. "Claude".
    string DisplayName { get; }

    // Whether this provider participates in the active set AT ALL. Read LIVE each poll — never cached.
    // The mock returns true only in demo mode (it would be noise otherwise); a real provider returns
    // true even when not signed in (see the type comment — missing credentials are reported via a
    // NotConfigured snapshot, not by disappearing). A future settings choice of which agents to show
    // would gate here.
    bool IsAvailable { get; }

    // When true this provider is the SOLE active source (the mock in demo mode). Default false.
    bool IsExclusive => false;

    // Fetch the current usage snapshot. MUST NOT throw for expected failures (no credentials, network,
    // 401, 429): return a Status-carrying snapshot instead (degrade-and-log). The repository
    // single-flights and serializes refreshes, so implementations need no internal concurrency guard.
    Task<DomainUsageSnapshot> GetUsageAsync(CancellationToken ct = default);
}
