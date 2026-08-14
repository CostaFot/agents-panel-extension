# Claude data source (Data/Claude/)

Read this before changing anything in `Data/Claude/`. Deliberately isolated: everything
endpoint-specific stays behind `IAgentUsageProvider` here, so a replacement source is a drop-in
sibling.

## Endpoint

`GET https://api.anthropic.com/api/oauth/usage`, `Authorization: Bearer <token>` from
`%USERPROFILE%\.claude\.credentials.json`, `anthropic-beta: oauth-2025-04-20`, honest UA
`agents-panel/<ver>` (**decision: no client spoofing**). ⚠️ **Undocumented endpoint, ToS-gray** for
published tools; it can change or vanish.

## Response shape

Two generations of shape: legacy named windows (`five_hour`, `seven_day`, `seven_day_opus/sonnet` —
the per-model ones are null now) AND the newer generic `limits` array (`kind`:
`session`/`weekly_all`/`weekly_scoped` + `scope.model.display_name`). **Map from `limits` when
present**; legacy is the fallback. Verified live 2026-08.

## Credentials

- **Never log the token**; read credentials at call time only.
- **NO token refresh** — undocumented rotation could log the user out of Claude Code. Expired ⇒
  `TokenExpired` status.

## Rate limits

Endpoint rate-limits hard: poll floor **3 min**, clamped in `UsageSettingsManager.RefreshMinutes`'s
getter (not just the placeholder). `HttpRetry` honors Retry-After, 3 attempts, 8s bail.

## Token log reader

`ClaudeTokenLogReader.cs` tails `%USERPROFILE%\.claude\projects\*\*.jsonl` with per-message-id
dedupe (streaming rewrites repeat message ids; last wins). Incremental tail mechanics: see
`notes/token-stats.md`.
