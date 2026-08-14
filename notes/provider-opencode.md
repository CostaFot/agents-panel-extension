# opencode data source (Data/OpenCode/) — LOCAL-ONLY, no HTTP at all

Read this before changing anything in `Data/OpenCode/`.

## Why local-only (the deliberate v1 shape)

**opencode has no balance/usage API**: `/zen/v1/balance` and `/zen/v1/usage` 404 (probed live
2026-08-15); upstream requests anomalyco/opencode **#10448** (Zen balance) + **#16017** (Go plan
windows) both open. The only remote source is the opencode.ai dashboard's cookie-authenticated
`_server?id=<build-hash>` RPC (what steipete/CodexBar scrapes on macOS) — **rejected**: Windows
browser-cookie decryption is invasive, and the function ids are frontend-build hashes that churn
every deploy. When the balance endpoint ships, the HTTP fetch drops into `OpenCodeUsageProvider`
as the quota path (auth.json API key, read at call time, never logged, no refresh) — same seam as
the other providers.

## Local data dir

xdg-style even on Windows: `XDG_DATA_HOME` ?? `%USERPROFILE%\.local\share`, + `\opencode`:

- `auth.json` (`OpenCodeCredentialsReader`) — a map `providerID → {type: api|oauth|wellknown, …}`;
  an `"opencode"` entry (Zen login = `type: "api"`) is the ONLY thing checked, purely for the
  NotConfigured/Ok pivot. **v1 never extracts the key** — there's nothing to spend it on.
- `opencode.db` (`OpenCodeUsageReader`, Microsoft.Data.Sqlite) — WAL-mode SQLite. The `message`
  table's assistant rows carry `data` JSON with `providerID`/`cost` (USD)/`tokens{input, output,
  reasoning, cache{read,write}}` and `time_created` in epoch **milliseconds** (verified live
  2026-08). One windowed query per poll (stateless — no tailing), `json_extract` in SQL so message
  CONTENT never leaves the database. ⚠️ The `session` table's counters are cumulative-per-session —
  can't be time-windowed; don't "simplify" to them.

## What it reports

- One `ExtraUsage` window = rolling-24h **gateway spend** (`Σ cost WHERE providerID = 'opencode'` —
  Zen credits / Go balance-fallback, the money that actually leaves the account;
  Go-subscription-covered messages record cost 0 locally so the sum stays honest) with
  `Qualifier "24h"` + `Unit "USD"` (renders "$0.13 used" / dock "24h $0.13" — `Unit` is what
  separates verified dollars from Copilot's unit-unknown bare "n used").
- TokenStats over ALL opencode messages (reasoning folds into output).
- No HTTP ⇒ status is only ever NotConfigured (no auth.json entry) or Ok; a failed DB read degrades
  to an empty-Ok snapshot (quiet absence).

## SQLite read strategy

`Mode=ReadOnly; Pooling=False` (no handle survives the poll), `CommandTimeout=2` (= busy timeout).
Read-only WAL access works mid-session (live `-shm`) and after clean exit; the crash-orphaned-wal
edge falls back to copying db+wal+shm to temp and querying the copy. Any failure → null this poll,
retried next.

## Naming

DisplayName/Id are `opencode` (lowercase brand; names the AGENT, not a billing product — Go and
Zen are billing modes of the same account, so a "Zen" row would age badly).
