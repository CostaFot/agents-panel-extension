using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentsPanelExtension;

// opencode usage from LOCAL data only — the deliberate v1 shape, because opencode exposes no
// balance/usage API at all: /zen/v1/balance and /zen/v1/usage 404 (probed live 2026-08), with both
// upstream requests still open (anomalyco/opencode #10448 Zen balance, #16017 Go plan windows).
// The only remote source is the opencode.ai dashboard's cookie-authenticated `_server` RPC, whose
// function ids are frontend-build hashes — rejected as both invasive (browser cookie extraction)
// and unstable. When the balance endpoint ships, the HTTP fetch drops in HERE as the quota path,
// using the auth.json API key (read at call time, never logged, no refresh) — same seam as the
// other providers.
//
// What v1 reports instead (OpenCodeUsageReader, opencode's local SQLite DB):
// - one ExtraUsage window: rolling-24h gateway spend in USD ("$0.12 used") — the pay-as-you-go
//   number that actually drains the account's Zen balance (Go-subscription-covered messages record
//   cost 0 locally, so the sum stays honest under either billing mode);
// - the standard 24h token stats, attached to every outcome per convention.
//
// No HTTP at all → works offline and can never be TokenExpired/RateLimited/Error; the only pivot
// is NotConfigured (no auth.json entry) vs Ok. A failed DB read degrades to an empty Ok snapshot
// (quiet absence), never an error surface.
internal sealed class OpenCodeUsageProvider : IAgentUsageProvider
{
    private const string Tag = "OpenCode";

    public string Id => "opencode";

    // The brand styles itself lowercase — and the row names the AGENT, not a billing product
    // (Go and Zen are billing modes of the same account; this must not age badly).
    public string DisplayName => "opencode";

    // Always in the active set, rendering "Sign in" when signed out (see IAgentUsageProvider).
    public bool IsAvailable => true;

    public Task<DomainUsageSnapshot> GetUsageAsync(CancellationToken ct = default)
    {
        // All-local, fast IO — no async work to await. The read runs regardless of sign-in state
        // so token stats ride NotConfigured too (same attachment convention as Claude/Codex).
        var read = OpenCodeUsageReader.ReadLast24Hours();

        DomainUsageSnapshot snapshot;
        if (!OpenCodeCredentialsReader.IsConfigured())
        {
            snapshot = DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.NotConfigured);
        }
        else
        {
            // Spend rides even at $0.00 — "nothing billed in the last 24h" is information, and a
            // constant row keeps the dock band's shape stable. Absent only when the DB read failed.
            IReadOnlyList<DomainUsageWindow> windows = read is { } r
                ? [new DomainUsageWindow(
                    "gateway_spend_24h", UsageWindowKind.ExtraUsage, Utilization: 0, ResetsAt: null,
                    Qualifier: "24h", Used: r.GatewaySpend, Limit: null, Unit: "USD")]
                : [];
            snapshot = new DomainUsageSnapshot(
                Id, DisplayName, UsageStatus.Ok, windows, PlanLabel: null, DateTimeOffset.UtcNow);
            Log.Info(Tag, $"local usage read ({windows.Count} window(s))");
        }

        return Task.FromResult(snapshot with { TokenStats = read?.Tokens });
    }
}
