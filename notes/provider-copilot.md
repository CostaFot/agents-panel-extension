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

1. `quota_snapshots` (all current plans; verified live 2026-08 on Free: chat 200 + completions
   2000 metered, premium_interactions zeroed) — skip buckets with `unlimited`, `has_quota=false`,
   or `entitlement<=0` (**a zeroed bucket's `percent_remaining: 0` is "not metered", NOT "100%
   used"**; credit-billed seats zero out all three — CodexBar#1258).
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
