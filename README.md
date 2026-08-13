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
- **Claude support (v1)**: reads the sign-in Claude Code already has on your machine — no setup, no
  API key. Shows the same numbers as Claude Code's `/usage`: session (5-hour), weekly, per-model
  weekly limits, and extra-usage credits.
- **Extensible by design**: providers implement a small `IAgentUsageProvider` interface; ChatGPT,
  Copilot, and friends can slot in later.
- **Demo mode**: built-in sample data to try the UI with no account at all.
- Stale-tolerant: if a refresh fails (offline, rate-limited), the last known numbers stay visible and
  are marked stale instead of vanishing.

## Requirements

- Windows 11, PowerToys with Command Palette (extension SDK ≥ 0.9 for Dock support)
- For live Claude data: [Claude Code](https://claude.com/claude-code) signed in with a Pro/Max account

## Notes

- The Claude usage endpoint is undocumented; this extension polls it gently (3-minute minimum,
  configurable) and identifies itself honestly (`agents-panel/<version>`). If the endpoint changes,
  the provider is isolated so it can be swapped without touching the rest of the app.
- Your OAuth token is read from Claude Code's local credential file at request time only — never
  stored, never logged, never sent anywhere except `api.anthropic.com`.
- No telemetry. Release builds log nothing.

## Building

```
dotnet build AgentsPanelExtension.sln
```

Deploy the MSIX from Visual Studio (the "(Package)" launch profile), then reload Command Palette.
