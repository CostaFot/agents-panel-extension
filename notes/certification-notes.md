# Certification test notes (Partner Center submission)

Paste (or adapt) the block below into the "Notes for certification" field. A Store reviewer has no
Claude/Codex/Copilot credentials, so the app will show three "Not signed in" rows — these notes explain
why that is the expected, fully functional state, and give the reviewer a way to exercise live data.

---

Agents Panel is a PowerToys Command Palette extension — it works only inside Command Palette and
shows usage data for AI subscriptions already signed in on the machine (Claude, Codex, GitHub
Copilot). No accounts, servers, or telemetry of its own.

**IMPORTANT: on a machine with no AI sign-ins, every row shows "Not signed in". That is the
correct, fully functional state — the app never shows fake data. Please do not fail it for this;
the listing discloses the subscription requirement.**

To test:

1. Install Microsoft PowerToys (free) and enable Command Palette. (Launching "Agents Panel" from
   the Start menu only shows a pointer to Command Palette — by design.)
2. Press **Win+Alt+Space**, type **Agents Panel**, press Enter → three provider rows, each "Not
   signed in" (expected). Enter on a row opens its detail page with a Refresh command.
3. Optional, to see live data at no cost: on a GitHub account with Copilot enabled (free tier is
   enough), create a fine-grained token at https://github.com/settings/personal-access-tokens
   with permission "Copilot Requests: Read", and paste it in Command Palette Settings →
   Extensions → Agents Panel. The Copilot row then shows live quota numbers.

runFullTrust: the app runs as an out-of-process COM server activated by the Command Palette host —
standard for all Command Palette extensions.

Privacy: tokens are sent only to the provider that issued them; no telemetry.
https://costafot.github.io/agents-panel-extension/privacy.html

---

Notes to self (not for the reviewer):

- The privacy URL above was verified live 2026-08-14 (Pages enabled + deployed, page serves the
  real policy).
- If certification pushes back on the "Not signed in" rows anyway, the fallback argument: the Store
  policy concern is apps that are non-functional without undisclosed purchases — the listing
  discloses the subscription requirement (B8) and the Copilot path above gives the reviewer a
  zero-cost way to see live data (free Copilot tier).
