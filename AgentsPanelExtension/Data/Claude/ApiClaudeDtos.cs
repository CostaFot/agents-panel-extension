using System.Text.Json.Serialization;

namespace AgentsPanelExtension;

// Raw DTOs for the two Claude-side JSON shapes this provider reads. Api* layer: mirrors the wire/file
// format exactly, every field nullable (the endpoint is UNDOCUMENTED — anything can be absent or null),
// no logic. Mapped to Domain* inside ClaudeUsageProvider / ClaudeCredentialsReader.

// Response of GET https://api.anthropic.com/api/oauth/usage — the same numbers Claude Code's /usage
// screen shows. utilization is 0–100; resets_at is ISO-8601 UTC.
//
// Two generations of shape coexist in the response (verified against a live 2026-08 response):
//  * the LEGACY named windows (five_hour, seven_day, seven_day_opus, seven_day_sonnet) — the per-model
//    ones are null on current accounts;
//  * the newer generic `limits` array (kind: session / weekly_all / weekly_scoped, percent, resets_at,
//    scope.model.display_name) — which is where per-model weekly limits actually live now, keyed by
//    display name (e.g. "Fable") rather than a hardcoded model set.
// The provider maps from `limits` when present and falls back to the named windows otherwise.
internal sealed record ApiClaudeUsageDto(
    [property: JsonPropertyName("five_hour")] ApiClaudeWindowDto? FiveHour,
    [property: JsonPropertyName("seven_day")] ApiClaudeWindowDto? SevenDay,
    [property: JsonPropertyName("seven_day_opus")] ApiClaudeWindowDto? SevenDayOpus,
    [property: JsonPropertyName("seven_day_sonnet")] ApiClaudeWindowDto? SevenDaySonnet,
    [property: JsonPropertyName("limits")] System.Collections.Generic.IReadOnlyList<ApiClaudeLimitDto>? Limits,
    [property: JsonPropertyName("extra_usage")] ApiClaudeExtraUsageDto? ExtraUsage);

internal sealed record ApiClaudeLimitDto(
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("group")] string? Group,
    [property: JsonPropertyName("percent")] double? Percent,
    [property: JsonPropertyName("resets_at")] string? ResetsAt,
    [property: JsonPropertyName("scope")] ApiClaudeLimitScopeDto? Scope);

internal sealed record ApiClaudeLimitScopeDto(
    [property: JsonPropertyName("model")] ApiClaudeLimitModelDto? Model);

internal sealed record ApiClaudeLimitModelDto(
    [property: JsonPropertyName("display_name")] string? DisplayName);

internal sealed record ApiClaudeWindowDto(
    [property: JsonPropertyName("utilization")] double? Utilization,
    [property: JsonPropertyName("resets_at")] string? ResetsAt);

internal sealed record ApiClaudeExtraUsageDto(
    [property: JsonPropertyName("is_enabled")] bool? IsEnabled,
    [property: JsonPropertyName("monthly_limit")] decimal? MonthlyLimit,
    [property: JsonPropertyName("used_credits")] decimal? UsedCredits,
    [property: JsonPropertyName("utilization")] double? Utilization);

// %USERPROFILE%\.claude\.credentials.json — Claude Code's local credential store on Windows. Only the
// fields this extension needs are declared; unknown fields are skipped by the serializer. The refresh
// token is DELIBERATELY not declared: this extension never refreshes tokens (an undocumented rotation
// dance that could log the user out of Claude Code), so the value is never even deserialized.
internal sealed record ApiClaudeCredentialsFileDto(
    [property: JsonPropertyName("claudeAiOauth")] ApiClaudeOauthDto? ClaudeAiOauth);

internal sealed record ApiClaudeOauthDto(
    [property: JsonPropertyName("accessToken")] string? AccessToken,
    [property: JsonPropertyName("expiresAt")] long? ExpiresAt,          // epoch ms (see ClaudeCredentialsReader.ToExpiry)
    [property: JsonPropertyName("subscriptionType")] string? SubscriptionType,
    [property: JsonPropertyName("rateLimitTier")] string? RateLimitTier);

// One line of a Claude Code transcript (%USERPROFILE%\.claude\projects\<slug>\<session>.jsonl) — only
// the sliver ClaudeTokenLogReader needs. Assistant lines carry message.usage (the API's own usage
// object); everything else (user lines, summaries, tool results) has Type != "assistant" or a null
// Usage and is skipped. The same message id can appear on SEVERAL lines (streaming rewrites, verified
// live 2026-08) — readers must dedupe by message id, keeping the LAST occurrence.
internal sealed record ApiClaudeTranscriptLineDto(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("timestamp")] string? Timestamp,      // ISO-8601 UTC
    [property: JsonPropertyName("requestId")] string? RequestId,
    [property: JsonPropertyName("message")] ApiClaudeTranscriptMessageDto? Message);

internal sealed record ApiClaudeTranscriptMessageDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("usage")] ApiClaudeTranscriptUsageDto? Usage);

internal sealed record ApiClaudeTranscriptUsageDto(
    [property: JsonPropertyName("input_tokens")] long? InputTokens,
    [property: JsonPropertyName("output_tokens")] long? OutputTokens,
    [property: JsonPropertyName("cache_read_input_tokens")] long? CacheReadInputTokens,
    [property: JsonPropertyName("cache_creation_input_tokens")] long? CacheCreationInputTokens);

// ⚠️ ALL [JsonSerializable] attributes for this context MUST stay on this ONE partial declaration.
// Splitting them across multiple partials silently breaks the source generator for the whole build
// (every context type comes back null at runtime) — MarketExtension hit exactly this.
[JsonSerializable(typeof(ApiClaudeUsageDto))]
[JsonSerializable(typeof(ApiClaudeCredentialsFileDto))]
[JsonSerializable(typeof(ApiClaudeTranscriptLineDto))]
internal sealed partial class ClaudeJsonContext : JsonSerializerContext;
