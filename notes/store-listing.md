# Microsoft Store listing copy

Compliance-reviewed copy for the Partner Center submission. Deliberately avoids: no
"official" anywhere, no "real-time"/"live" guarantees (the endpoints are undocumented and can lag
or vanish), no implied affiliation with Anthropic/OpenAI/GitHub/Microsoft, nominative trademark
use only — extra care since all three data endpoints are ToS-gray; required disclosures included
(third-party subscriptions needed, unofficial data source).

## Short description (search-results summary)

See your Claude, Codex, and GitHub Copilot usage limits inside PowerToys Command Palette — session and weekly quotas, reset times, and pinnable dock buttons.

## Description (main listing field)

**Agents Panel for Command Palette**

Keep an eye on your AI coding agents' usage quotas right inside Microsoft PowerToys Command Palette — how much of your session and weekly limits you've used, when each window resets, and your plan, without switching apps.

• One hub with a row per agent — Claude, Codex, and GitHub Copilot — each showing a usage summary at a glance
• Drill into any agent for every quota window, reset times, and plan details
• Pin per-agent dock bands for quick-look buttons like "5h 23%" and "Wk 41%" that update while pinned
• Uses the sign-ins your machine already has (Claude Code, the Codex CLI or app, GitHub tooling) — or paste a GitHub fine-grained token for Copilot
• If a refresh fails, the last known numbers stay visible and are marked stale instead of vanishing
• No accounts of its own, no telemetry — it runs entirely on your machine and talks only to each provider's own API

This app displays usage for subscriptions you already hold. It requires an active sign-in or subscription with the respective provider — a Claude plan (via Claude Code), a ChatGPT plan (via Codex), or GitHub Copilot — to show live data; without one, an agent's row simply offers sign-in guidance. Your tokens stay on your device and are sent only to the provider that issued them.

**Independent project.** Agents Panel is an open-source, independent extension. It is not affiliated with, endorsed by, or sponsored by Anthropic, OpenAI, GitHub, or Microsoft. Claude, ChatGPT, Codex, GitHub Copilot, PowerToys, and all other product names and logos are the property of their respective owners and are used only to identify the services whose usage the app displays.

**Disclaimer.** Usage figures come from endpoints the providers do not document for third-party use; they may be delayed, inaccurate, or incomplete, and a provider may change or remove them at any time, at which point the app may stop showing data for that agent. The numbers shown in each provider's own apps are authoritative — do not rely on this app as your only indicator of remaining quota.

## Compliance notes

- **No "official"** — anywhere, ever; the whole listing leans on "independent"/"unofficial".
- **No "real-time"/"live" guarantees** — polling is minutes-coarse and the endpoints are
  undocumented; "update while pinned" and the delay disclaimer carry the honest version.
- **Third-party account disclosure** — Store policy requires disclosing that third-party
  subscriptions/sign-ins are needed; the "requires an active sign-in or subscription" paragraph
  covers it explicitly for all three providers.
- **Undocumented-endpoint disclaimer** — explicit, including that data can stop working entirely;
  this also pre-answers a certification question about what happens without credentials (sign-in
  guidance rows, see the certification notes in `notes/certification-notes.md`).
- **Trademarks** — nominative use only ("for Command Palette", "your Claude … usage"), plus the
  explicit non-affiliation + owners'-rights paragraph naming all four companies.
- **Never claim token safety beyond what's true** — the Copilot PAT pasted in settings is stored
  in plain text locally; the listing says only "stay on your device", which is accurate, and the
  privacy policy discloses the plaintext detail.
