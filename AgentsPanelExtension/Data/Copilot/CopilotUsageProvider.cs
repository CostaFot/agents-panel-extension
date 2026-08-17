using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentsPanelExtension;

// GitHub Copilot usage via the copilot_internal/user endpoint — the same quota data GitHub's own
// Copilot clients (and Oh My Posh's copilot segment) read, authenticated with a GitHub token the
// machine already holds (CopilotCredentialsReader) or a user-supplied fine-grained PAT.
//
// ⚠️ The endpoint is UNDOCUMENTED and unsupported for third parties, same tier as the Claude and
// Codex sources: it can change shape (the 2026-06 move to AI-credit billing already reshaped it),
// tighten auth, or vanish without notice. Everything endpoint-specific therefore lives in this
// Data/Copilot/ folder behind IAgentUsageProvider, so a replacement source (e.g. the official
// billing REST API or the Copilot SDK's account.getQuota) is a drop-in sibling class.
//
// Failure mapping (degrade-and-log, never throw): no usable token → NotConfigured; 401/403 →
// TokenExpired (covers revoked tokens and token types the endpoint rejects); surviving 429 →
// RateLimited; anything else → Error. The repository's keep-last-good merge holds the previous
// numbers under the new status.
internal sealed class CopilotUsageProvider : IAgentUsageProvider
{
    private const string Tag = "Copilot";

    private const string UsageEndpoint = "https://api.github.com/copilot_internal/user";

    // Deliberate decision for this endpoint: no Editor-Version masquerade either — it answers
    // plain user agents (AppInfo.UserAgent).

    // One client for the process. Headers that never change ride on it; Authorization is
    // per-request because the token is re-discovered on every fetch.
    private static readonly HttpClient Http = CreateClient();

    public string Id => "copilot";

    public string DisplayName => "Copilot";

    // Always in the active set, rendering "Sign in" when no token is found rather than
    // disappearing (see IAgentUsageProvider).
    public bool IsAvailable => true;

    public async Task<DomainUsageSnapshot> GetUsageAsync(CancellationToken ct = default)
    {
        // Discover a token at call time — a sign-in/sign-out (or PAT change) applies on the next
        // poll, no reload. GitHub OAuth tokens are opaque (no JWT claims), so there's no local
        // expiry check: a dead token surfaces as a 401/403 below.
        var credentials = CopilotCredentialsReader.Read();
        if (credentials is null)
            return DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.NotConfigured);

        try
        {
            // Thunk builds a FRESH request per attempt (HttpRequestMessage is single-use).
            // NEVER log the request headers — the Authorization header carries the token.
            using var response = await HttpRetry.SendAsync(
                c =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.Token);
                    return Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, c);
                },
                Tag,
                ct).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                Log.Warn(Tag, $"usage request rejected ({(int)response.StatusCode}) via {credentials.Source} — treating as expired/invalid token");
                return DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.TokenExpired);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.RateLimited);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warn(Tag, $"usage request failed with {(int)response.StatusCode}");
                return DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.Error);
            }

            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var dto = await JsonSerializer.DeserializeAsync(
                stream, CopilotJsonContext.Default.ApiCopilotUserDto, ct).ConfigureAwait(false);
            if (dto is null)
            {
                Log.Warn(Tag, "usage response deserialized to null");
                return DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.Error);
            }

            var snapshot = new DomainUsageSnapshot(
                Id, DisplayName, UsageStatus.Ok, MapWindows(dto), PlanLabel(dto), DateTimeOffset.UtcNow);
            Log.Info(Tag, $"usage fetched ({snapshot.Windows.Count} window(s), plan={snapshot.PlanLabel ?? "?"})");
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw; // cancellation is not a failure — let it propagate
        }
        catch (Exception ex)
        {
            Log.Error(Tag, "usage fetch failed", ex);
            return DomainUsageSnapshot.Failed(Id, DisplayName, UsageStatus.Error);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", AppInfo.UserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    // All Copilot quotas reset on the same monthly date → every window is Month-kind; the Qualifier
    // tells multiple monthly buckets apart ("Chat" / "Code" — terse because they double as the dock
    // button label inside its ~15-char budget; "Code" = code completions). premium_interactions
    // stays unqualified: on the plans where it's metered it's the ONLY window, so plain "Mo" reads
    // best.
    private static IReadOnlyList<DomainUsageWindow> MapWindows(ApiCopilotUserDto dto)
    {
        var windows = new List<DomainUsageWindow>(3);
        var resetsAt = ParseResetDate(dto.QuotaResetDateUtc) ?? ParseResetDate(dto.QuotaResetDate);

        // Token-based billing means NO metered buckets, whatever the snapshot/legacy fields claim —
        // skip them entirely and publish plan info only (plus the absolute credits counter when
        // present), never usage fabricated from placeholder snapshots. Possibly zero windows: the
        // hub row then falls back to status text, which is the honest rendering.
        if (dto.TokenBasedBilling == true)
        {
            if (dto.CreditsUsed is { } tokenBillingCredits)
            {
                windows.Add(new DomainUsageWindow(
                    "credits", UsageWindowKind.ExtraUsage, 0, resetsAt, Qualifier: null,
                    Used: tokenBillingCredits, Limit: null));
            }
            return windows;
        }

        // Current generations report every metered bucket through quota_snapshots regardless of
        // plan (verified live 2026-08 on a Free sku: chat + completions metered, premium zeroed).
        AddSnapshot(windows, dto.QuotaSnapshots?.PremiumInteractions, "premium_interactions", qualifier: null, resetsAt);
        AddSnapshot(windows, dto.QuotaSnapshots?.Chat, "chat", "Chat", resetsAt);
        AddSnapshot(windows, dto.QuotaSnapshots?.Completions, "completions", "Code", resetsAt);

        // Older free-tier payloads: flat remaining (limited_user_quotas) / entitlement
        // (monthly_quotas) maps instead of snapshots.
        if (windows.Count == 0 && dto.LimitedUserQuotas is { } remaining)
        {
            var limitedReset = ParseResetDate(dto.LimitedUserResetDate) ?? resetsAt;
            AddLimited(windows, "chat", "Chat", remaining.Chat, dto.MonthlyQuotas?.Chat, limitedReset);
            AddLimited(windows, "completions", "Code", remaining.Completions, dto.MonthlyQuotas?.Completions, limitedReset);
        }

        // Credit-billed seats: snapshots come back zeroed (entitlement 0 — filtered out above) and
        // an absolute credits_used counter appears instead. No entitlement is reported, so this
        // renders as an ExtraUsage row ("12.3 used") rather than a fabricated percentage — the
        // utilization stays 0 because the cap is unknown.
        if (windows.Count == 0 && dto.CreditsUsed is { } credits)
        {
            windows.Add(new DomainUsageWindow(
                "credits", UsageWindowKind.ExtraUsage, 0, resetsAt, Qualifier: null, Used: credits, Limit: null));
        }

        return windows;
    }

    private static void AddSnapshot(
        List<DomainUsageWindow> windows, ApiCopilotQuotaDetailDto? dto, string id, string? qualifier, DateTimeOffset? resetsAt)
    {
        // unlimited / has_quota=false / entitlement 0 all mean "not metered", and their
        // percent_remaining of 0 must NOT render as a 100%-used window (free plans zero out
        // premium_interactions this way; credit-billed seats zero out all three). Some
        // token-billing/Business seats report the INVERSE placeholder — entitlement 0 with
        // percent_remaining 100 and a real quota_id — which would render a misleading "0% used";
        // the entitlement gate below drops it before percent_remaining is ever read.
        if (dto is null || dto.Unlimited == true || dto.HasQuota == false)
            return;
        var entitlement = dto.Entitlement ?? 0;
        if (entitlement <= 0)
            return;

        double used;
        if (dto.PercentRemaining is { } percentRemaining)
            used = 100 - percentRemaining;
        else if ((dto.Remaining ?? dto.QuotaRemaining) is { } left)
            used = 100 * (1 - (left / entitlement));
        else
            return;

        windows.Add(new DomainUsageWindow(id, UsageWindowKind.Month, Clamp(used), resetsAt, qualifier));
    }

    private static void AddLimited(
        List<DomainUsageWindow> windows, string id, string qualifier, double? remaining, double? entitlement, DateTimeOffset? resetsAt)
    {
        if (remaining is not { } left || entitlement is not { } total || total <= 0)
            return;
        windows.Add(new DomainUsageWindow(id, UsageWindowKind.Month, Clamp(100 * (1 - (left / total))), resetsAt, qualifier));
    }

    private static double Clamp(double usedPercent) => Math.Clamp(usedPercent, 0, 100);

    // quota_reset_date_utc is a full ISO timestamp; quota_reset_date / limited_user_reset_date are
    // bare dates ("2026-09-01") — both parse here, bare dates as UTC midnight. Unparseable → null
    // (the window just shows no reset line).
    private static DateTimeOffset? ParseResetDate(string? value) =>
        DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    // access_type_sku pins down the Free tier explicitly (its copilot_plan reads "individual",
    // which would mislabel it); everything else prettifies copilot_plan, passing unknown values
    // through as-is so a new plan name still shows SOMETHING sensible.
    private static string? PlanLabel(ApiCopilotUserDto dto)
    {
        if (string.Equals(dto.AccessTypeSku, "free_limited_copilot", StringComparison.OrdinalIgnoreCase))
            return "Free";
        return dto.CopilotPlan switch
        {
            null or "" => null,
            "free" => "Free",
            "individual" => "Individual",
            "pro" => "Pro",
            "pro_plus" => "Pro+",
            "max" => "Max",
            "business" => "Business",
            "enterprise" => "Enterprise",
            var other => other,
        };
    }
}
