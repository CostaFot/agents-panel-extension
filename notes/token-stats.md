# Token stats (since 2026-08-15)

Read this before changing any token-stats reader or its formatting.

## Model plumbing

`DomainTokenStats` rides `DomainUsageSnapshot.TokenStats` (nullable — absent means "provider has no
local logs / read failed", a QUIET absence, never an error surface). Readers are attached AROUND
the quota fetch so they ride every outcome incl. NotConfigured/TokenExpired. The repository merge
takes the FRESH TokenStats even under keep-last-good (local logs succeed when HTTP fails).

## Windowing

All readers sum a **rolling 24h** window — deliberately not calendar-day: a midnight reset zeroes
the row mid-session.

## Readers

- The Claude/Codex JSONL readers tail incrementally: per-file byte offset; only newline-complete
  lines consumed; shrunken files reparsed from zero.
- `Data/Claude/ClaudeTokenLogReader.cs` — `.claude\projects\*\*.jsonl`, per-message-id dedupe
  (streaming rewrites repeat message ids; last wins). Details: `notes/provider-claude.md`.
- `Data/Codex/CodexTokenLogReader.cs` — `token_count` events; ⚠️ diffs the CUMULATIVE
  `total_token_usage` into deltas, never sums; fresh input = input − cached. Details:
  `notes/provider-codex.md`.
- opencode: stateless windowed SQLite query, not a tail — `notes/provider-opencode.md`.
- Copilot has no local logs; stays null.

## Formatting (all in `UiUsage`)

- `TokenSummary` — "56k in · 29k out · 2.2M cache read"; in = fresh input + cache WRITES — raw
  input_tokens is a misleading crumb under prompt caching.
- `TokenDockTitle`/`TokenTotalText` — "24h 2.3M" / page-row tag — the TOTAL of all four counters,
  the ecosystem's glance number; any single component reads as noise at first sight.

## Settings

Two settings: ShowTokenStats (page row) and subordinate ShowTokenStatsInDock (dock button;
requires the first).
