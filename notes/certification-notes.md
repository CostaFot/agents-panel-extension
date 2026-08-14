# Certification test notes (Partner Center submission)

Paste (or adapt) the block below into the "Notes for certification" field. A Store reviewer has no
Claude/Codex/Copilot credentials, so the app will show three "Not signed in" rows — these notes explain
why that is the expected, fully functional state, and give the reviewer a way to exercise live data.

---

**What this app is.** Agents Panel is a PowerToys Command Palette extension (it requires Microsoft
PowerToys with Command Palette to be installed and running). It displays the usage quotas of AI
coding agents the user already subscribes to — Claude, Codex (ChatGPT), and GitHub Copilot — by
reading the sign-in credentials those providers' own apps store locally on the machine and querying
each provider's usage API directly. The app has no accounts, servers, or telemetry of its own.

**How to launch it.** Install Microsoft PowerToys, enable Command Palette, install this package,
then open Command Palette (default Win+Alt+Space) and run "Agents Panel". Each provider also
exposes a dock band that can be pinned via Command Palette's dock.

**What you will see without any AI-agent credentials.** One row per provider (Claude, Codex,
GitHub Copilot), each reading "Not signed in to <provider> — No sign-in found on this PC". This is
the designed behavior for a machine that has no AI-agent subscriptions — the app
deliberately does not ship fake data. All navigation, settings, refresh, and dock-pinning behavior
works in this state.

**How to see live data (optional, Copilot is the easiest).** In the app's settings (Command
Palette → Agents Panel → Settings), paste a GitHub fine-grained personal access token that has the
account permission "Copilot Requests: Read" for any GitHub account with Copilot enabled (the free
Copilot tier is sufficient). The Copilot row will then display live monthly quota data. Claude and
Codex data appear only when Claude Code or the Codex CLI/app is signed in on the machine.

**Why runFullTrust.** The app is a Command Palette extension: it runs as an out-of-process COM
server that the PowerToys Command Palette host activates, which requires the full-trust
application capability (standard for all Command Palette extensions).

**Privacy.** Tokens are read at request time only and sent only to the provider that issued them
(api.anthropic.com, chatgpt.com, api.github.com). No telemetry. Privacy policy:
https://costafot.github.io/agents-panel-extension/privacy.html

---

Notes to self (not for the reviewer):

- The privacy URL above assumes GitHub Pages is enabled on the repo (B5); verify it resolves
  before submitting.
- If certification pushes back on the "Not signed in" rows anyway, the fallback argument: the Store
  policy concern is apps that are non-functional without undisclosed purchases — the listing
  discloses the subscription requirement (B8) and the Copilot path above gives the reviewer a
  zero-cost way to see live data (free Copilot tier).
