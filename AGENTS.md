# Agents Panel Extension for Command Palette — Agent Guide

Single source of truth for coding agents (Claude Code, Codex, …) working in this repo — CLAUDE.md
just imports this file.

A PowerToys **Command Palette** extension showing AI-agent **usage quotas** — session/weekly % used,
reset times, plan info — with pinnable per-provider **Dock bands** as the main selling point
("5h 23%" / "Wk 41%" quick-look buttons; one band per provider, so the user picks which agents to
pin via the host's own band management). Covers **Claude** (Pro/Max subscription limits), **Codex**
(ChatGPT subscription limits), and **Copilot** (GitHub Copilot quotas); the provider architecture is
open for more later. .NET 9 / C# / MSIX,
self-contained single-file JIT (trim/AOT deliberately OFF).

## Reference project — use it A LOT

Scaffolded from **MarketExtension** (`C:\Users\jarla\code\MarketExtension`, same author). When in doubt,
look there first — its `CLAUDE.md` + `notes/` document hard-won CmdPal knowledge (`notes/cmdpal-toolkit.md`
before fighting any toolkit behavior, `notes/releasing.md` for the release flow). Its `reference/` folder
holds the pristine AdbExtension blank-extension files.

## Architecture in one screen

Single observable source of truth; every surface OBSERVES, none fetches:

- `Data/UsageRepository.cs` — **the coordinator the UI depends on**: owns one
  `MutableStateFlow<IReadOnlyList<DomainUsageSnapshot>>` (the whole cache — no DynamicData/per-key
  streams), a permanent `PollTicker` subscription whose handler no-ops at 0 observers, single-flight
  refresh, **keep-last-good merge** (a failed poll never blanks good numbers — old windows survive with
  the new `UsageStatus` riding on them), and `ObserveUsage()` ending in the **load-bearing
  `ObserveOn(TaskPoolScheduler.Default)`** hop. `ObserveUsage(providerId)` is a thin projection layered
  ON TOP of it (inherits refcounting + the hop; emits null while the provider is absent) — keep it that
  way.
- `Data/IAgentUsageProvider.cs` — the extension seam: `Id`, `DisplayName`, `IsAvailable` (participates
  at all — read live), `GetUsageAsync` (**never throws** for expected failures; returns a
  `UsageStatus`-carrying snapshot). ⚠️ Unlike MarketExtension there is NO
  fallback routing: an unconfigured provider stays active and reports `NotConfigured` (rendered as a
  "Sign in" row/button) instead of disappearing — agent providers aren't interchangeable.
- Registration order in `AgentsPanelCommandsProvider`: `ClaudeUsageProvider` → `CodexUsageProvider`
  → `CopilotUsageProvider`, in the static `Providers` array (used twice: repository ctor + one dock
  band per entry). Add
  providers to that array, plus a PNG + case in `Helpers/ProviderIcons.cs` (ProviderId → icon;
  identifies dock buttons/hub rows — the terse dock titles carry no provider identity) — the hub row
  and dock band then appear automatically.
- Model layering (MarketExtension convention): `Api*Dto` (wire format, all-nullable) → `Domain*`
  (provider-agnostic, NO formatting) → `Ui*` (`Models/UiUsage.cs` — the ONLY formatting home). Enums
  (`UsageWindowKind`, `UsageStatus`) unprefixed.
- Surfaces (two-level since 2026-08): `Pages/UsagePage.cs` (hub = **provider list**, one row per
  provider with a "5h 23% · Wk 41%" summary subtitle + worst-window/status pills; also the top-level
  command) → `Pages/UsageProviderPage.cs` (per-provider drill-in: window rows, status/plan rows,
  Refresh — dock buttons bypass the hub, so those can't be hub-only) → and
  `Pages/UsageDockPage.cs` (**one instance per provider** since 2026-08-13, observing
  `ObserveUsage(providerId)`; each `GetItems()` row = one dock button, title budget ~15 chars,
  **deep-links to that provider's page**; per-band `Id` = `…agentspanel.dock.<providerId>` — must
  stay unique. Deliberately NO "show provider X in dock" setting: the host's pin/unpin per band IS
  the chooser — ShowTokenStatsInDock is different, it toggles a button WITHIN a band). All three
  implement the explicit
  `INotifyItemsChanged` pattern: subscribe `ObserveUsage(...)` in the event's `add` accessor,
  dispose-all in `remove`, `List<IDisposable>` (host may add twice), `private new void
  RaiseItemsChanged`. `Pages/UsageProviderPageCache.cs` holds ONE `UsageProviderPage` per ProviderId,
  shared by hub AND dock (built in `AgentsPanelCommandsProvider`) so both navigate into the same
  instance; lazy, never evicts — an unobserved page holds no repository observer, so stale entries
  are free.
- Provider-row summary/filter formatting lives in `UiUsage` (`VisibleWindows`/`SummaryText`/
  `WorstVisibleWindow` — the ShowModelWindows/ShowExtraUsage filter has ONE home now); status
  pill/subtitle extraction lives in `UsageStatusHint.StatusTag`/`StatusSubtitle` (used by `StatusRow`
  too — don't re-fork the colors/strings).
- **Token stats** (since 2026-08-15): `DomainTokenStats` rides `DomainUsageSnapshot.TokenStats`
  (nullable — absent means "provider has no local logs / read failed", a QUIET absence, never an
  error surface). Both readers sum a **rolling 24h** window (deliberately not calendar-day: a
  midnight reset zeroes the row mid-session), tail incrementally (per-file byte offset; only
  newline-complete lines consumed; shrunken files reparsed from zero), and are attached AROUND the
  quota fetch so they ride every outcome incl. NotConfigured/TokenExpired. The repository merge takes
  the FRESH TokenStats even under keep-last-good (local logs succeed when HTTP fails).
  `Data/Claude/ClaudeTokenLogReader.cs` tails `%USERPROFILE%\.claude\projects\*\*.jsonl` with
  per-message-id dedupe (streaming rewrites repeat message ids; last wins).
  `Data/Codex/CodexTokenLogReader.cs` tails `%CODEX_HOME%\sessions\yyyy\mm\dd\rollout-*.jsonl`
  `token_count` events — ⚠️ `total_token_usage` is CUMULATIVE per session, so it DIFFS consecutive
  events into timestamped deltas (never sums; rebaselines on a counter reset), and Codex's
  `input_tokens` INCLUDES the cached subset, so fresh input = input − cached. Formatting:
  `UiUsage.TokenSummary` ("56k in · 29k out · 2.2M cache read"; in = fresh input + cache WRITES — raw
  input_tokens is a misleading crumb under prompt caching) + `TokenDockTitle` ("24h 29k", output
  only — title budget). Two settings: ShowTokenStats (page row) and subordinate ShowTokenStatsInDock
  (dock button; requires the first). Copilot has no local logs; stays null.

## The Claude data source (Data/Claude/ — deliberately isolated)

`GET https://api.anthropic.com/api/oauth/usage`, `Authorization: Bearer <token>` from
`%USERPROFILE%\.claude\.credentials.json`, `anthropic-beta: oauth-2025-04-20`, honest UA
`agents-panel/<ver>` (**decision: no client spoofing**). ⚠️ **Undocumented endpoint, ToS-gray** for
published tools; it can change or vanish — that's why everything endpoint-specific stays behind
`IAgentUsageProvider` in `Data/Claude/`, so a replacement source is a drop-in sibling.

- Response has TWO generations of shape: legacy named windows (`five_hour`, `seven_day`,
  `seven_day_opus/sonnet` — the per-model ones are null now) AND the newer generic `limits` array
  (`kind`: `session`/`weekly_all`/`weekly_scoped` + `scope.model.display_name`). **Map from `limits`
  when present**; legacy is the fallback. Verified live 2026-08.
- **Never log the token**; read credentials at call time only; NO token refresh (undocumented rotation
  could log the user out of Claude Code) — expired ⇒ `TokenExpired` status.
- Endpoint rate-limits hard: poll floor **3 min**, clamped in `UsageSettingsManager.RefreshMinutes`'s
  getter (not just the placeholder). `HttpRetry` honors Retry-After, 3 attempts, 8s bail.

## The Codex data source (Data/Codex/ — deliberately isolated, mirrors Data/Claude/)

`GET https://chatgpt.com/backend-api/wham/usage`, `Authorization: Bearer <access_token>` +
`ChatGPT-Account-Id: <account_id>` from `%CODEX_HOME%\auth.json` (default `%USERPROFILE%\.codex`),
honest UA. Same ⚠️ **undocumented, ToS-gray** tier as the Claude endpoint (paths/fields/plan_type
values have all churned across 2025–2026); reference implementations: openai/codex
`codex-rs/backend-client` + steipete/CodexBar.

- **Window shape varies BY PLAN** (why `UsageWindowKind` picks from `limit_window_seconds`, never the
  primary/secondary slot): Plus/Pro report 5h primary + weekly secondary; the Go plan reports ONE
  30-day primary (→ `Month` kind, "Mo" dock label) and a null secondary. Verified live 2026-08 on Go.
- Reset time: `reset_at` (epoch seconds) when present, else `now + reset_after_seconds` — both
  generations exist in the wild.
- **Never log the token**; read credentials at call time; NO token refresh — a harder no than Claude:
  Codex **rotates refresh tokens**, so a third-party refresh that doesn't write back perfectly logs
  the user out of Codex itself. Expired ⇒ `TokenExpired`; Codex's own use refreshes it (~8-day cadence).
  Plan type + expiry come from the access token's JWT claims (decoded unvalidated, local trust);
  `id_token`/`refresh_token` are deliberately never deserialized.
- On this dev machine Codex is the MSIX desktop app (`OpenAI.Codex`) — no `codex` CLI on PATH and
  WindowsApps ACLs block spawning its bundled codex.exe, which is why the `codex app-server` JSON-RPC
  route was rejected in favor of direct HTTP.

## The Copilot data source (Data/Copilot/ — deliberately isolated, mirrors Data/Codex/)

`GET https://api.github.com/copilot_internal/user`, `Authorization: Bearer <token>`, honest UA —
deliberately NO Editor-Version masquerade (the endpoint answers plain user agents; Oh My Posh's
copilot segment ships the same call). Same ⚠️ **undocumented, ToS-gray** tier as the other two
(GitHub has never sanctioned third-party use of `copilot_internal`; the 2026-06 move to AI-credit
billing already reshaped the response once). Reference implementations: Oh My Posh `copilot`
segment, steipete/CodexBar, ericc-ch/copilot-api.

- **Response shape varies BY PLAN and by billing generation** — `MapWindows` branches, in order:
  (1) `quota_snapshots` (all current plans; verified live 2026-08 on Free: chat 200 + completions
  2000 metered, premium_interactions zeroed) — skip buckets with `unlimited`, `has_quota=false`, or
  `entitlement<=0` (**a zeroed bucket's `percent_remaining: 0` is "not metered", NOT "100% used"**;
  credit-billed seats zero out all three — CodexBar#1258); (2) legacy free-tier
  `limited_user_quotas`/`monthly_quotas` maps; (3) top-level `credits_used` → an ExtraUsage
  "n used" row (cap/units unreported — no fabricated percentage). All windows are `Month` kind
  (one shared monthly reset: `quota_reset_date_utc` → `quota_reset_date` → `limited_user_reset_date`);
  `Qualifier` ("Chat"/"Code") tells multiple monthly buckets apart — `UiUsage` renders a qualified
  Month like a qualified ModelWeek.
- **Credential discovery chain** (`CopilotCredentialsReader`, first usable wins): settings PAT
  override (`UsageSettingsManager.CopilotToken` — plain text in agentspanel.settings.json) → env
  `COPILOT_GITHUB_TOKEN`/`GH_TOKEN`/`GITHUB_TOKEN` → Windows Credential Manager (one full
  enumerate matching by target-name shape, **observed live**: Copilot CLI =
  `<uuid>.github-copilot-app` — a SUFFIX match, CredEnumerate wildcards are prefix-only — and gh
  secure storage = `gh:github.com:<user>`, go-keyring `service:user` naming, so exact `CredRead`
  misses it; blob may be raw token or a JSON wrapper — both handled) → `%APPDATA%\GitHub CLI\
  hosts.yml` (hand-parsed, GH_CONFIG_DIR honored; empty of tokens when secure storage is in use) →
  `%LOCALAPPDATA%\github-copilot\apps.json`/`hosts.json`. Classic `ghp_` PATs are skipped
  everywhere (endpoint rejects them); `gho_`/`ghu_` OAuth tokens and fine-grained PATs
  (*Copilot Requests: Read*) work.
- **Never log the token** (log only the source label); read at call time; NO token refresh — these
  are other apps' long-lived OAuth tokens. GitHub tokens are opaque (no JWT claims), so there's no
  local expiry check: a dead/rejected token surfaces as 401/403 ⇒ `TokenExpired`.

## Build & Deploy

`dotnet build AgentsPanelExtension.sln -p:Platform=x64` — ⚠️ without the platform flag MSBuild picks
**ARM64** (alphabetically first in the sln) on this x64 machine and the package won't deploy.
Settings persist to `…\Microsoft.CmdPal\agentspanel.settings.json`.

⚠️ **Agents: BUILD ONLY — never deploy/register the package.** The developer runs and deploys the
extension from **Rider**; deployment is their job, not the agent's. Verification stops at a clean
`dotnet build` — after that, tell the user "ready to deploy from Rider" and let them click through.
Lesson learned (2026-08): an agent ran `Add-AppxPackage -Register` over an existing Rider
deployment — it first failed with 0x80073D02 (package files locked by the running
`AgentsPanelExtension.exe`; that lock is EXPECTED, not a problem to bulldoze), then after
force-killing the process the register "succeeded" and left **three duplicate "Agents Panel"
entries** in the palette; the user had to uninstall everything and redeploy from Rider to recover.
So:

- Do NOT run `Add-AppxPackage` (any form), and do NOT `Stop-Process` the extension to free the
  package — a locked package means a live deployment that isn't yours to replace.
- Even if the user explicitly asks you to deploy: first check the existing install with
  `Get-AppxPackage -Name '*AgentsPanel*'`, and if one exists, confirm they want it replaced
  (they'll likely need to remove it and redeploy from Rider afterwards).

## Project conventions

- New commands → `Commands/`, extend `InvokableCommand`. New pages → `Pages/`.
- No hardcoded user-facing strings — `Properties/Resources.resx` + hand-maintained
  `Resources.Designer.cs` (dotnet build does NOT regen it; keep them in lock-step or regen from VS).
- Logging: `Log.Info/Warn/Error(tag, msg)`, all `[Conditional("DEBUG")]` — Release ships silent.
- Error surfaces are **status rows** (`Helpers/UsageStatusHint.cs`: red=broken,
  amber=degraded-self-healing), never toasts for background failures.
- **Fail loud** on genuinely-wrong states; degrade-and-log for expected external failures.

## Gotchas (inherited from MarketExtension — all live here too)

- ⚠️ **The Write tool drops Segoe MDL2 glyph chars.** Always write them as `\uXXXX` escapes and
  byte-check after editing. (Bit us during initial development of this very repo.)
- ⚠️ **`ObserveOn(TaskPoolScheduler.Default)` in `UsageRepository.ObserveUsage` is load-bearing** —
  removing it (or "fixing" with `SubscribeOn`) re-creates the Rx-gate↔STA deadlock that hangs CmdPal.
  Don't add `Task.Run` on the delivery path either.
- ⚠️ **Page-activation:** `GetItems()` runs before `ItemsChanged` is subscribed — a constructor
  `RaiseItemsChanged` is lost. First paint must come from the `add`-accessor subscription replay.
- ⚠️ **Dock bands require a non-empty command `Id`** or the band silently disappears — and with one
  band per provider the `Id` must also be **unique per band** or the host conflates them.
- ⚠️ **JSON source-gen:** all `[JsonSerializable]` for one context on a SINGLE partial declaration
  (`Data/Claude/ApiClaudeDtos.cs`, `Data/Codex/ApiCodexDtos.cs` — one context per provider) —
  splitting silently breaks the generator.
- ⚠️ **`TextSetting` renders `Description` as the on-screen label** (Label is ignored by Input.Text).
- **PollTicker source operators must never throw** — a throw OnErrors the multicast and kills polling
  for every subscriber until reload. Settings reads stay parse-with-fallback.
- AOT/trim intentionally OFF — reflection-based code is fine.

## Git

- Never amend commits (`git commit --amend`) — always create a new commit, unless explicitly asked to
  amend in the moment.
- **Never commit on your own** — leave changes in the working tree and let the user review; commit
  only when explicitly asked in that moment (a general "commit when done" in a plan does not carry
  over to later work).
- **Never run GitHub workflows** (`gh workflow run`, `gh run rerun`, or anything else that triggers
  CI/releases) — those are the user's to fire, only on an explicit ask in the moment.
