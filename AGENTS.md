# Agents Panel Extension for Command Palette — Agent Guide

Single source of truth for coding agents (Claude Code, Codex, …) working in this repo — CLAUDE.md
just imports this file. Deep provider/token-stats detail lives in `notes/` (see below); read the
matching note BEFORE changing anything under `Data/<Provider>/`.

A PowerToys **Command Palette** extension showing AI-agent **usage quotas** — session/weekly % used,
reset times, plan info — with pinnable per-provider **Dock bands** as the main selling point
("5h 23%" / "Wk 41%" quick-look buttons; one band per provider, so the user picks which agents to
pin via the host's own band management). Covers **Claude**, **Codex**, **Copilot**, and
**opencode**; the provider architecture is open for more later. .NET 9 / C# / MSIX, self-contained
single-file JIT (trim/AOT deliberately OFF).

## Reference project — use it A LOT

Scaffolded from **MarketExtension** (`C:\Users\jarla\code\MarketExtension`, same author). When in
doubt, look there first — its `CLAUDE.md` + `notes/` document hard-won CmdPal knowledge
(`notes/cmdpal-toolkit.md` before fighting any toolkit behavior, `notes/releasing.md` for the
release flow). Its `reference/` folder holds the pristine AdbExtension blank-extension files.

## Architecture in one screen

Single observable source of truth; every surface OBSERVES, none fetches:

- `Data/UsageRepository.cs` — **the coordinator the UI depends on**: owns one
  `MutableStateFlow<IReadOnlyList<DomainUsageSnapshot>>` (the whole cache — no DynamicData/per-key
  streams), a permanent `PollTicker` subscription whose handler no-ops at 0 observers,
  single-flight refresh, **keep-last-good merge** (a failed poll never blanks good numbers — old
  windows survive with the new `UsageStatus` riding on them), and `ObserveUsage()` ending in the
  **load-bearing `ObserveOn(TaskPoolScheduler.Default)`** hop. `ObserveUsage(providerId)` is a
  thin projection layered ON TOP of it (inherits refcounting + the hop; emits null while the
  provider is absent) — keep it that way.
- `Data/IAgentUsageProvider.cs` — the extension seam: `Id`, `DisplayName`, `IsAvailable`
  (participates at all — read live), `GetUsageAsync` (**never throws** for expected failures;
  returns a `UsageStatus`-carrying snapshot). ⚠️ Unlike MarketExtension there is NO fallback
  routing: an unconfigured provider stays active and reports `NotConfigured` (rendered as a
  "Sign in" row/button) instead of disappearing — agent providers aren't interchangeable.
- Registration: `ClaudeUsageProvider` → `CodexUsageProvider` → `CopilotUsageProvider` →
  `OpenCodeUsageProvider` in `AgentsPanelCommandsProvider`'s static `Providers` array (used twice:
  repository ctor + one dock band per entry). To add a provider: add it to that array, plus a PNG +
  case in `Helpers/ProviderIcons.cs` (ProviderId → icon; identifies dock buttons/hub rows — the
  terse dock titles carry no provider identity) — the hub row and dock band then appear
  automatically.
- Model layering (MarketExtension convention): `Api*Dto` (wire format, all-nullable) → `Domain*`
  (provider-agnostic, NO formatting) → `Ui*` (`Models/UiUsage.cs` — the ONLY formatting home).
  Enums (`UsageWindowKind`, `UsageStatus`) unprefixed.
- Surfaces (two-level since 2026-08): `Pages/UsagePage.cs` (hub = **provider list**, one row per
  provider with a "5h 23% · Wk 41%" summary subtitle + worst-window/status pills; also the
  top-level command) → `Pages/UsageProviderPage.cs` (per-provider drill-in: window rows,
  status/plan rows, Refresh — dock buttons bypass the hub, so those can't be hub-only) →
  `Pages/UsageDockPage.cs` (**one instance per provider** since 2026-08-13, observing
  `ObserveUsage(providerId)`; each `GetItems()` row = one dock button, title budget ~15 chars,
  **deep-links to that provider's page**; per-band `Id` = `…agentspanel.dock.<providerId>` — must
  stay unique. Deliberately NO "show provider X in dock" setting: the host's pin/unpin per band IS
  the chooser — ShowTokenStatsInDock is different, it toggles a button WITHIN a band). All three
  implement the explicit `INotifyItemsChanged` pattern: subscribe `ObserveUsage(...)` in the
  event's `add` accessor, dispose-all in `remove`, `List<IDisposable>` (host may add twice),
  `private new void RaiseItemsChanged`. `Pages/UsageProviderPageCache.cs` holds ONE
  `UsageProviderPage` per ProviderId, shared by hub AND dock (built in
  `AgentsPanelCommandsProvider`) so both navigate into the same instance; lazy, never evicts — an
  unobserved page holds no repository observer, so stale entries are free.
- Provider-row summary/filter formatting lives in `UiUsage` (`VisibleWindows`/`SummaryText`/
  `WorstVisibleWindow` — the ShowModelWindows/ShowExtraUsage filter has ONE home now); status
  pill/subtitle extraction lives in `UsageStatusHint.StatusTag`/`StatusSubtitle` (used by
  `StatusRow` too — don't re-fork the colors/strings).
- **Token stats** (since 2026-08-15): `DomainTokenStats` rides `DomainUsageSnapshot.TokenStats`
  (nullable — absent is a QUIET absence, never an error surface); all readers sum a **rolling 24h**
  window; formatting (`TokenSummary`/`TokenDockTitle`/`TokenTotalText`) lives in `UiUsage`.
  ⚠️ Reader mechanics are subtle (incremental tails, cumulative-counter diffing, dedupe) — read
  `notes/token-stats.md` before touching any of it.

## Data sources — read the note before touching

Each provider is deliberately isolated behind `IAgentUsageProvider` in its own `Data/<Name>/`
folder, so a replacement source is a drop-in sibling. All HTTP endpoints are ⚠️ **undocumented and
ToS-gray** — they can change or vanish. Shared credential rules, no exceptions: **never log a
token**, read credentials at call time only, **NO token refresh** (each provider's note explains
why its refresh is dangerous); expired ⇒ `TokenExpired`. Honest UA, no client spoofing.

- **Claude** (`Data/Claude/`, `notes/provider-claude.md`) — `api.anthropic.com/api/oauth/usage`
  with the Claude Code OAuth token. ⚠️ Response has two shape generations — map from `limits` when
  present; poll floor 3 min (endpoint rate-limits hard).
- **Codex** (`Data/Codex/`, `notes/provider-codex.md`) — `chatgpt.com/backend-api/wham/usage` with
  the Codex auth.json token. ⚠️ Window shape varies by plan — pick `UsageWindowKind` from
  `limit_window_seconds`, never the primary/secondary slot.
- **Copilot** (`Data/Copilot/`, `notes/provider-copilot.md`) — `api.github.com/copilot_internal/
  user` via a five-step credential discovery chain. ⚠️ A zeroed quota bucket means "not metered",
  NOT "100% used".
- **opencode** (`Data/OpenCode/`, `notes/provider-opencode.md`) — LOCAL-ONLY (no usage API exists
  yet): auth.json for the NotConfigured/Ok pivot + windowed SQLite reads of `opencode.db`.
  ⚠️ The `session` table's counters are cumulative-per-session — don't "simplify" to them.

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
  removing it (or "fixing" with `SubscribeOn`) re-creates the Rx-gate↔STA deadlock that hangs
  CmdPal. Don't add `Task.Run` on the delivery path either.
- ⚠️ **Page-activation:** `GetItems()` runs before `ItemsChanged` is subscribed — a constructor
  `RaiseItemsChanged` is lost. First paint must come from the `add`-accessor subscription replay.
- ⚠️ **Dock bands require a non-empty command `Id`** or the band silently disappears — and with one
  band per provider the `Id` must also be **unique per band** or the host conflates them.
- ⚠️ **JSON source-gen:** all `[JsonSerializable]` for one context on a SINGLE partial declaration
  (`Data/Claude/ApiClaudeDtos.cs`, `Data/Codex/ApiCodexDtos.cs` — one context per provider) —
  splitting silently breaks the generator.
- ⚠️ **`TextSetting` renders `Description` as the on-screen label** (Label is ignored by Input.Text).
- **PollTicker source operators must never throw** — a throw OnErrors the multicast and kills
  polling for every subscriber until reload. Settings reads stay parse-with-fallback.
- AOT/trim intentionally OFF — reflection-based code is fine.

## Git

- Never amend commits (`git commit --amend`) — always create a new commit, unless explicitly asked
  to amend in the moment.
- **Never commit on your own** — leave changes in the working tree and let the user review; commit
  only when explicitly asked in that moment (a general "commit when done" in a plan does not carry
  over to later work).
- **Never run GitHub workflows** (`gh workflow run`, `gh run rerun`, or anything else that triggers
  CI/releases) — those are the user's to fire, only on an explicit ask in the moment.
