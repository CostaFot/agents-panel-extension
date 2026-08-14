# Agents Panel for Command Palette

A [PowerToys Command Palette](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/overview)
extension that shows your AI agents' **usage and limits** at a glance — how much of your session and
weekly quota is used, when each window resets, and your plan — right from the palette and, most
usefully, from the **Command Palette Dock**.

## Features

- **Dock band** (the main event): pinnable quick-look buttons like `5h 23%` and `Wk 41%` that
  live-update while pinned and click through to the full panel.
- **Agents Panel hub**: one row per quota window with color-coded severity (green → amber → red),
  reset times, plan info, and a manual refresh.
- **Claude**: reads the sign-in Claude Code already has on your machine — no setup, no API key.
  Shows the same numbers as Claude Code's `/usage`: session (5-hour), weekly, per-model weekly
  limits, and extra-usage credits.
- **Codex**: reads the Codex CLI/app sign-in (ChatGPT subscription limits) — session, weekly, or
  monthly windows depending on plan.
- **Copilot**: reads a GitHub token the machine already has (gh CLI, Copilot CLI, or the editor
  Copilot plugins — or paste a fine-grained PAT in settings) and shows the monthly Copilot quotas
  (chat / completions / premium requests, per plan).
- **Extensible by design**: providers implement a small `IAgentUsageProvider` interface; more agents
  can slot in later.
- Stale-tolerant: if a refresh fails (offline, rate-limited), the last known numbers stay visible and
  are marked stale instead of vanishing.

## Requirements

- Windows 11, PowerToys with Command Palette (extension SDK ≥ 0.9 for Dock support)
- For live Claude data: [Claude Code](https://claude.com/claude-code) signed in with a Pro/Max account
- For live Codex data: the Codex CLI or desktop app signed in with a ChatGPT account
- For live Copilot data: any local GitHub sign-in (gh CLI, Copilot CLI, editor Copilot plugins) or a
  fine-grained PAT with *Copilot Requests: Read*

## Notes

- The usage endpoints are undocumented; this extension polls them gently (3-minute minimum,
  configurable) and identifies itself honestly (`agents-panel/<version>`). If an endpoint changes,
  each provider is isolated so it can be swapped without touching the rest of the app.
- Tokens are read from the agents' own local credential stores at request time only — never
  stored, never logged, never sent anywhere except each agent's own usage endpoint
  (`api.anthropic.com`, `chatgpt.com`, `api.github.com`).
- No telemetry. Release builds log nothing.

## Building

```
dotnet build AgentsPanelExtension.sln -p:Platform=x64
```

Deploy the MSIX package from your IDE (Rider or Visual Studio's "(Package)" launch profile), then
reload Command Palette.
