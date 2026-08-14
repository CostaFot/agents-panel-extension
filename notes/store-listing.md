# Microsoft Store listing copy

Compliance-reviewed copy for the Partner Center submission. Structure copied exactly from
MarketExtension's shipped listing (one-liner → bullets → key-requirement paragraph → open-source +
source link → non-affiliation → disclaimer → Requirements), which went through Store review easily
while naming its providers freely. Deliberately avoids: no "official" anywhere, no
"real-time"/"live" guarantees (the endpoints are undocumented and can lag or vanish), no implied
affiliation, nominative trademark use only — extra care since all three data endpoints are
ToS-gray; required disclosures included (third-party subscriptions needed, unofficial data source).

## Short description (search-results summary)

Track your AI coding agents' usage right inside Command Palette.

## Search terms (Partner Center allows 7 × ≤30 chars, ≤21 words total)

1. Claude usage
2. Codex usage
3. Copilot usage
4. AI agent quota
5. usage limits
6. PowerToys
7. Command Palette extension

Provider names deliberately live here (and in the full description) since the short description
dropped them — these carry the "claude usage"-style search queries.

## Description (main listing field)

Track your AI coding agents' usage right inside Command Palette.

• See session and weekly quotas, reset times, and plan info for Claude, Codex, and GitHub Copilot
• Pin per-agent quick-look buttons like "5h 23%" and "Wk 41%" to the Command Palette dock
• No accounts, no tracking, no data collection

To view usage data you'll need your own subscription and sign-in with each provider you want to track, used under your own agreement with that provider. Your tokens and data stay on your device — the extension has no servers of its own and sends each token only to the provider it belongs to.

This is an open-source, independent extension. Source code is available at https://github.com/CostaFot/agents-panel-extension.

It is not affiliated with, endorsed by, or sponsored by Microsoft, Anthropic, OpenAI, GitHub, or any AI provider. Company and product names and logos are the property of their respective owners.

Disclaimer: Usage figures are not guaranteed and can be inaccurate or incomplete. This extension is for informational purposes only.

Requirements
• PowerToys 0.98.1 or later, with Command Palette enabled

## Compliance notes

- **Structure copied exactly from MarketExtension's shipped listing** — same sections, same order,
  same register (terse opener, tight bullets, "used under your own agreement with that provider",
  "no servers of its own", "Requirements" block). That listing passed certification naming
  Finnhub/Twelve Data/Frankfurter freely — precedent that nominative naming is fine.
- **Name frequency minimized** (2026-08-14 decision): providers are named once in the body (the
  first bullet); the non-affiliation paragraph names the companies (mirroring Markets' "or any
  exchange or data provider" with "or any AI provider"); every other paragraph says
  "provider"/"agent". The short description drops the names too (2026-08-14 final, user call —
  matches the description opener verbatim); provider-name search relies on the full description.
- **No "official"** — anywhere, ever; the whole listing leans on "independent"/"unofficial".
- **No "real-time"/"live" guarantees** — polling is minutes-coarse and the endpoints are
  undocumented; the delay disclaimer carries the honest version.
- **Third-party account disclosure** — Store policy requires disclosing that third-party
  subscriptions/sign-ins are needed; the "your own subscription and sign-in with each provider"
  paragraph covers it (providers enumerated in the first bullet).
- **Disclaimer kept short by user decision (2026-08-14 final)** — "not guaranteed and can be
  inaccurate or incomplete … informational purposes only". The undocumented-endpoint/can-vanish
  detail was deliberately dropped from the listing; it still lives in
  `notes/certification-notes.md` (for the reviewer) and the docs pages.
- **No signed-out claims** — the final copy says nothing about what a signed-out row shows,
  which stays consistent with C10 (rows are neutral, no sign-in guidance; an earlier draft's
  "offers sign-in guidance" line was wrong).
- **No demo-mode bullet** — Markets had one; this app deliberately has no demo mode (2026-08-14
  decision), certification notes carry that weight instead.
- **Requirements line** — PowerToys 0.98.1+ (Dock + `GetDockBands` shipped in PowerToys 0.98 /
  CmdPal 0.9; same line MarketExtension shipped).
- **Never claim token safety beyond what's true** — the Copilot PAT pasted in settings is stored
  in plain text locally; the listing says only "stay on your device", which is accurate, and the
  privacy policy discloses the plaintext detail.
