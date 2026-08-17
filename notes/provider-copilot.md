# Copilot data source (Data/Copilot/)

Read this before changing anything in `Data/Copilot/`. Deliberately isolated, mirrors `Data/Codex/`.

## Endpoint

`GET https://api.github.com/copilot_internal/user`, `Authorization: Bearer <token>`, honest UA —
deliberately NO Editor-Version masquerade (the endpoint answers plain user agents; Oh My Posh's
copilot segment ships the same call). Same ⚠️ **undocumented, ToS-gray** tier as the other
providers (GitHub has never sanctioned third-party use of `copilot_internal`; the 2026-06 move to
AI-credit billing already reshaped the response once). Reference implementations: Oh My Posh
`copilot` segment, steipete/CodexBar, ericc-ch/copilot-api.

## Response shape

**Varies BY PLAN and by billing generation** — `MapWindows` branches, in order:

⚠️ **Do NOT branch on the top-level `token_based_billing` flag** (won't-do, learned the hard way
2026-08-18): a "true ⇒ no metered buckets" short-circuit was shipped and immediately blanked real
windows — verified live on this machine's Free/individual seat, which reports
`token_based_billing: true` at top level AND per snapshot while chat (193.8/200) and completions
(2000/2000) are fully metered. The flag marks the post-AI-credits billing *generation*, not "not
metered"; the per-snapshot gate below is the only reliable meter signal. The field is deliberately
not declared in the DTO. (Per-snapshot `credits_used` counters also exist in this generation —
unrendered for now.) NB the bug was in OUR port, not CodexBar: their `CopilotUsageFetcher.swift`
consults the flag only as an else-if AFTER snapshot mapping yields zero windows, purely to tell
"legitimately unmetered → plan-only" from "unrecognized response → error" — a distinction our
model doesn't need (all-snapshots-dropped already renders honestly as Ok with empty windows).

1. `quota_snapshots` (all current plans; verified live 2026-08 on Free: chat 200 + completions
   2000 metered, premium_interactions zeroed) — skip buckets with `unlimited`, `has_quota=false`,
   or `entitlement<=0` (**a zeroed bucket's `percent_remaining: 0` is "not metered", NOT "100%
   used"**; credit-billed seats zero out all three — CodexBar#1258). Some token-billing/Business
   seats report the INVERSE placeholder — `entitlement: 0` with `percent_remaining: 100` and a
   real `quota_id` — which would render a misleading "0% used"; the `entitlement<=0` gate drops it
   before `percent_remaining` is ever read (a strict superset of CodexBar's placeholder test, so
   no `quota_id` field is needed). When `percent_remaining` is absent, used% derives from
   `remaining`/`entitlement`; when neither is derivable the bucket is OMITTED, never fabricated
   as 0.
2. Legacy free-tier `limited_user_quotas`/`monthly_quotas` maps.
3. Top-level `credits_used` → an ExtraUsage "n used" row (cap/units unreported — no fabricated
   percentage).

All windows are `Month` kind (one shared monthly reset: `quota_reset_date_utc` →
`quota_reset_date` → `limited_user_reset_date`); `Qualifier` ("Chat"/"Code") tells multiple monthly
buckets apart — `UiUsage` renders a qualified Month like a qualified ModelWeek.

## Credential discovery chain

`CopilotCredentialsReader`, first usable wins:

1. Settings PAT override (`UsageSettingsManager.CopilotToken` — plain text in
   agentspanel.settings.json).
2. Env `COPILOT_GITHUB_TOKEN`/`GH_TOKEN`/`GITHUB_TOKEN`.
3. Windows Credential Manager — one full enumerate matching by target-name shape, **observed
   live**: Copilot CLI = `<uuid>.github-copilot-app` — a SUFFIX match, CredEnumerate wildcards are
   prefix-only — and gh secure storage = `gh:github.com:<user>`, go-keyring `service:user` naming,
   so exact `CredRead` misses it; blob may be raw token or a JSON wrapper — both handled.
4. `%APPDATA%\GitHub CLI\hosts.yml` (hand-parsed, GH_CONFIG_DIR honored; empty of tokens when
   secure storage is in use).
5. `%LOCALAPPDATA%\github-copilot\apps.json`/`hosts.json`.

Classic `ghp_` PATs are skipped everywhere (endpoint rejects them); `gho_`/`ghu_` OAuth tokens and
fine-grained PATs (*Copilot Requests: Read*) work.

## Credentials rules

- **Never log the token** (log only the source label); read at call time.
- **NO token refresh** — these are other apps' long-lived OAuth tokens.
- GitHub tokens are opaque (no JWT claims), so there's no local expiry check: a dead/rejected token
  surfaces as 401/403 ⇒ `TokenExpired`.

## Token stats

Copilot has no local logs; `TokenStats` stays null.
