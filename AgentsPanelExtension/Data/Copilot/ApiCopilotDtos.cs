using System.Text.Json.Serialization;

namespace AgentsPanelExtension;

// Raw DTOs for the Copilot-side JSON shapes this provider reads. Api* layer: mirrors the wire/file
// format exactly, every field nullable (the endpoint is UNDOCUMENTED — anything can be absent or
// null), no logic. Mapped to Domain* inside CopilotUsageProvider / CopilotCredentialsReader.

// Response of GET https://api.github.com/copilot_internal/user — the quota data GitHub's own Copilot
// clients read. The shape varies BY PLAN and has churned across billing-model generations:
//  - Free (verified live 2026-08 on this sku): quota_snapshots with metered chat + completions and a
//    zeroed premium_interactions (has_quota=false), plus quota_reset_date(_utc).
//  - Paid legacy request-billing: quota_snapshots where premium_interactions is the metered one and
//    chat/completions are unlimited.
//  - Credit-billed seats (post-2026-06 "AI Credits"): quota snapshots all report entitlement 0 (a
//    known upstream quirk — steipete/CodexBar#1258); a top-level credits_used counter appears instead.
//  - Older free-tier payloads: limited_user_quotas / monthly_quotas / limited_user_reset_date with no
//    quota_snapshots at all.
// Fields we don't render (endpoints, feature flags, org lists, ...) are deliberately not declared.
internal sealed record ApiCopilotUserDto(
    [property: JsonPropertyName("copilot_plan")] string? CopilotPlan,
    [property: JsonPropertyName("access_type_sku")] string? AccessTypeSku,
    [property: JsonPropertyName("quota_snapshots")] ApiCopilotQuotaSnapshotsDto? QuotaSnapshots,
    [property: JsonPropertyName("quota_reset_date")] string? QuotaResetDate,          // "2026-09-01"
    [property: JsonPropertyName("quota_reset_date_utc")] string? QuotaResetDateUtc,   // full ISO timestamp
    [property: JsonPropertyName("credits_used")] decimal? CreditsUsed,
    [property: JsonPropertyName("limited_user_quotas")] ApiCopilotLimitedQuotasDto? LimitedUserQuotas,
    [property: JsonPropertyName("monthly_quotas")] ApiCopilotLimitedQuotasDto? MonthlyQuotas,
    [property: JsonPropertyName("limited_user_reset_date")] string? LimitedUserResetDate);

internal sealed record ApiCopilotQuotaSnapshotsDto(
    [property: JsonPropertyName("chat")] ApiCopilotQuotaDetailDto? Chat,
    [property: JsonPropertyName("completions")] ApiCopilotQuotaDetailDto? Completions,
    [property: JsonPropertyName("premium_interactions")] ApiCopilotQuotaDetailDto? PremiumInteractions);

// One quota bucket. percent_remaining is the primary signal; remaining/entitlement are the raw
// counts backing it (used as a fallback when percent_remaining is absent). has_quota=false and
// unlimited=true both mean "nothing to meter here" — NOT "100% used", even though percent_remaining
// reads 0 in those payloads.
internal sealed record ApiCopilotQuotaDetailDto(
    [property: JsonPropertyName("entitlement")] double? Entitlement,
    [property: JsonPropertyName("remaining")] double? Remaining,
    [property: JsonPropertyName("quota_remaining")] double? QuotaRemaining,
    [property: JsonPropertyName("percent_remaining")] double? PercentRemaining,
    [property: JsonPropertyName("unlimited")] bool? Unlimited,
    [property: JsonPropertyName("has_quota")] bool? HasQuota);

// Older free-tier quota maps: limited_user_quotas holds REMAINING counts, monthly_quotas the
// entitlements. Same two buckets as the snapshot form.
internal sealed record ApiCopilotLimitedQuotasDto(
    [property: JsonPropertyName("chat")] double? Chat,
    [property: JsonPropertyName("completions")] double? Completions);

// One entry of %LOCALAPPDATA%\github-copilot\apps.json (keyed "github.com:<clientId>") or
// hosts.json (keyed "github.com") — the editor Copilot plugins' credential stores. Root shape for
// both files is a string → entry dictionary (declared on the context below).
internal sealed record ApiCopilotAppEntryDto(
    [property: JsonPropertyName("user")] string? User,
    [property: JsonPropertyName("oauth_token")] string? OAuthToken);

// ⚠️ ALL [JsonSerializable] attributes for this context MUST stay on this ONE partial declaration.
// Splitting them across multiple partials silently breaks the source generator for the whole build
// (every context type comes back null at runtime) — MarketExtension hit exactly this.
[JsonSerializable(typeof(ApiCopilotUserDto))]
[JsonSerializable(typeof(System.Collections.Generic.Dictionary<string, ApiCopilotAppEntryDto>))]
internal sealed partial class CopilotJsonContext : JsonSerializerContext;
