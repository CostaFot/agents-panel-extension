using System.Text.Json.Serialization;

namespace AgentsPanelExtension;

// Raw DTOs for the Codex-side JSON shapes this provider reads. Api* layer: mirrors the wire/file
// format exactly, every field nullable (the endpoint is UNDOCUMENTED — anything can be absent or
// null), no logic. Mapped to Domain* inside CodexUsageProvider / CodexCredentialsReader.

// Response of GET https://chatgpt.com/backend-api/wham/usage — the same numbers the Codex CLI's
// /status screen shows. Window shape and count vary BY PLAN (verified live 2026-08): a Go plan
// reports one 30-day primary_window and secondary_window null; Plus/Pro report the familiar 5-hour
// primary + weekly secondary. used_percent is an integer 0–100; reset_at is epoch SECONDS (newer
// builds; older payloads only have reset_after_seconds — the provider handles both). Fields we don't
// render (credits, spend_control, code_review_rate_limit, ...) are deliberately not declared.
internal sealed record ApiCodexUsageDto(
    [property: JsonPropertyName("plan_type")] string? PlanType,
    [property: JsonPropertyName("rate_limit")] ApiCodexRateLimitDto? RateLimit,
    [property: JsonPropertyName("additional_rate_limits")] System.Collections.Generic.IReadOnlyList<ApiCodexAdditionalRateLimitDto>? AdditionalRateLimits);

internal sealed record ApiCodexRateLimitDto(
    [property: JsonPropertyName("primary_window")] ApiCodexWindowDto? PrimaryWindow,
    [property: JsonPropertyName("secondary_window")] ApiCodexWindowDto? SecondaryWindow);

internal sealed record ApiCodexWindowDto(
    [property: JsonPropertyName("used_percent")] double? UsedPercent,
    [property: JsonPropertyName("limit_window_seconds")] long? LimitWindowSeconds,
    [property: JsonPropertyName("reset_after_seconds")] long? ResetAfterSeconds,
    [property: JsonPropertyName("reset_at")] long? ResetAt); // epoch seconds

// Model/feature-scoped extra limits (e.g. a per-model cap). Same window shape one level down.
internal sealed record ApiCodexAdditionalRateLimitDto(
    [property: JsonPropertyName("limit_name")] string? LimitName,
    [property: JsonPropertyName("rate_limit")] ApiCodexRateLimitDto? RateLimit);

// %CODEX_HOME%\auth.json (default %USERPROFILE%\.codex) — the Codex CLI/desktop app's local
// credential store. Only the fields this extension needs are declared. The refresh token and
// id_token are DELIBERATELY not declared: this extension never refreshes tokens (Codex refresh
// tokens ROTATE on use — a refresh without a perfect write-back would log the user out of Codex),
// and the plan-type claim we want is already on the access token.
internal sealed record ApiCodexAuthFileDto(
    [property: JsonPropertyName("auth_mode")] string? AuthMode,
    [property: JsonPropertyName("tokens")] ApiCodexTokensDto? Tokens);

internal sealed record ApiCodexTokensDto(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("account_id")] string? AccountId);

// The access token's JWT payload (decoded WITHOUT signature validation — we only need two claims
// from a file we already trust). exp is epoch seconds; the plan type hides in the OpenAI
// auth-namespace claim object.
internal sealed record ApiCodexJwtPayloadDto(
    [property: JsonPropertyName("exp")] long? Exp,
    [property: JsonPropertyName("https://api.openai.com/auth")] ApiCodexJwtAuthClaimDto? Auth);

internal sealed record ApiCodexJwtAuthClaimDto(
    [property: JsonPropertyName("chatgpt_plan_type")] string? ChatGptPlanType);

// ⚠️ ALL [JsonSerializable] attributes for this context MUST stay on this ONE partial declaration.
// Splitting them across multiple partials silently breaks the source generator for the whole build
// (every context type comes back null at runtime) — MarketExtension hit exactly this.
[JsonSerializable(typeof(ApiCodexUsageDto))]
[JsonSerializable(typeof(ApiCodexAuthFileDto))]
[JsonSerializable(typeof(ApiCodexJwtPayloadDto))]
internal sealed partial class CodexJsonContext : JsonSerializerContext;
