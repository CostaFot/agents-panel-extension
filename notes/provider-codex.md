# Codex data source (Data/Codex/)

Read this before changing anything in `Data/Codex/`. Deliberately isolated, mirrors `Data/Claude/`.

## Endpoint

`GET https://chatgpt.com/backend-api/wham/usage`, `Authorization: Bearer <access_token>` +
`ChatGPT-Account-Id: <account_id>` from `%CODEX_HOME%\auth.json` (default `%USERPROFILE%\.codex`),
honest UA. Same ⚠️ **undocumented, ToS-gray** tier as the Claude endpoint (paths/fields/plan_type
values have all churned across 2025–2026). Reference implementations: openai/codex
`codex-rs/backend-client` + steipete/CodexBar.

**Base-URL override** (since 2026-08, ported from CodexBar): `CodexConfigReader` reads
`chatgpt_base_url` from `%CODEX_HOME%\config.toml` at call time — self-hosted/enterprise Codex
points its backend elsewhere and would otherwise silently 404. Path rule: a base containing
`/backend-api` serves `/wham/usage`; any other base serves the same data at `/api/codex/usage`.
Parse is deliberately naive (no TOML lib): only a top-level `chatgpt_base_url = "..."` line before
the first `[table]` header counts (a same-named key inside `[model_providers.*]` must NOT win);
anything missing/invalid → default base. Only the override's HOST is logged, never the full URL.

## Response shape

- **Window shape varies BY PLAN** (why `UsageWindowKind` picks from `limit_window_seconds`, never
  the primary/secondary slot): Plus/Pro report 5h primary + weekly secondary; the Go plan reports
  ONE 30-day primary (→ `Month` kind, "Mo" dock label) and a null secondary. Verified live 2026-08
  on Go.
- Reset time: `reset_at` (epoch seconds) when present, else `now + reset_after_seconds` — both
  generations exist in the wild.
- **Weekly caps session** (2026-08, ported from CodexBar): when the weekly window is at 100% with a
  FUTURE reset, the session window is forced to 100% at map time — the session lane keeps reporting
  headroom the server won't actually honor until the weekly reset. Reset times stay real on both
  rows; a null/past weekly reset leaves the session alone (stale/ambiguous data). Recomputed from
  live numbers every fresh Ok poll, so it un-forces itself after the reset.
- **`additional_rate_limits[]` decodes lossily** (2026-08): the DTO holds raw `JsonElement`s and
  the provider deserializes each element separately (skip-and-log on mismatch) so one malformed
  entry can't fail the whole response or its siblings. ⚠️ This makes
  `ApiCodexAdditionalRateLimitDto` unreachable from the root DTO — it has its own
  `[JsonSerializable]` entry, which must stay on the SINGLE partial `CodexJsonContext` declaration.

## Credentials

- **Never log the token**; read credentials at call time.
- **NO token refresh** — a harder no than Claude: Codex **rotates refresh tokens**, so a
  third-party refresh that doesn't write back perfectly logs the user out of Codex itself.
  Expired ⇒ `TokenExpired`; Codex's own use refreshes it (~8-day cadence).
- Plan type + expiry come from the access token's JWT claims (decoded unvalidated, local trust);
  `id_token`/`refresh_token` are deliberately never deserialized.

## Dev-machine quirk

On this dev machine Codex is the MSIX desktop app (`OpenAI.Codex`) — no `codex` CLI on PATH and
WindowsApps ACLs block spawning its bundled codex.exe, which is why the `codex app-server` JSON-RPC
route was rejected in favor of direct HTTP.

## Token log reader

`CodexTokenLogReader.cs` tails `%CODEX_HOME%\sessions\yyyy\mm\dd\rollout-*.jsonl` `token_count`
events. ⚠️ `total_token_usage` is CUMULATIVE per session, so it DIFFS consecutive events into
timestamped deltas (never sums; rebaselines on a counter reset), and Codex's `input_tokens`
INCLUDES the cached subset, so fresh input = input − cached. Incremental tail mechanics: see
`notes/token-stats.md`.
