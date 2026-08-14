# Agents Panel for Command Palette

[![GitHub release](https://img.shields.io/github/v/release/CostaFot/agents-panel-extension?style=flat-square&logo=github&label=release)](https://github.com/CostaFot/agents-panel-extension/releases/latest)
[![GitHub downloads](https://img.shields.io/github/downloads/CostaFot/agents-panel-extension/total?style=flat-square&logo=github&label=downloads)](https://github.com/CostaFot/agents-panel-extension/releases)

<img src="listing/screenshot_dock.png" width="600"/>

A Windows 11 [Command Palette](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/overview) (PowerToys) extension that shows your AI agents' **usage and limits** at a glance — right on the Command Palette dock.

> Not affiliated with, endorsed by, or sponsored by Anthropic, OpenAI, GitHub, or Microsoft. All product names are used only to identify the services this extension reads usage data for.

## Requirements

- [PowerToys](https://github.com/microsoft/PowerToys) with Command Palette enabled
- For live Claude data: Claude Code signed in with a Pro/Max account
- For live Codex data: the Codex CLI or desktop app signed in with a ChatGPT account
- For live Copilot data: a local GitHub Copilot sign-in, or a fine-grained PAT with *Copilot Requests: Read*

## Installation

<!-- TODO after Store approval: uncomment and fill in the Store ID.
### Microsoft Store
<a href="https://apps.microsoft.com/detail/<STORE_ID>" target="_self">
	<img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>
</a>

### WinGet

```powershell
winget install --id CostaFotiadis.AgentsPanelForCommandPalette
```
-->

Coming to the Microsoft Store and WinGet — until then, grab the UNSIGNED `.msixbundle` from [Releases](https://github.com/CostaFot/agents-panel-extension/releases).

## Features

### The Agents Panel hub

One top-level **Agents Panel** command opens the hub — one row per agent with a usage summary and the worst window's percentage at a glance.

<img src="listing/screenshot_panel_hub.png" width="500"/>

### Claude

Reads local configuration — no setup, no API key. Shows the same numbers as Claude Code's `/usage`: session (5-hour), weekly, per-model weekly limits, and extra-usage credits.

<img src="listing/screenshot_claude_hub.png" width="500"/>

### Codex

Reads local configuration and shows your ChatGPT subscription limits — session, weekly, or monthly windows depending on plan.

<img src="listing/screenshot_codex_hub.png" width="500"/>

### Copilot

Reads local configuration — or a fine-grained PAT pasted in settings — and shows the monthly Copilot quotas (chat / completions / premium requests, per plan).

<img src="listing/screenshot_copilot_hub.png" width="500"/>

### Dock integration

The main event: each agent is its own dock band with quick-look buttons like `5h 27%` and `Wk 17%` that live-update while pinned and click through to the full panel. Pin exactly the agents you want via the dock's own band management.

<img src="listing/screenshot_dock.png" width="600"/>

<img src="listing/screenshot_bands.png" width="400"/>

## FAQ

**Will you steal my tokens?**
I don't care about your tokens. Local configuration is read at request time only. Read the source code.

**Is this official?**
No. The usage endpoints are undocumented; this extension polls them (3-minute minimum, configurable). NO GUARANTEES :)

**Is there telemetry?**
No. 

**Do I need an account?**
Only the subscriptions of the agents you already use.

## Building

```
dotnet build AgentsPanelExtension.sln -p:Platform=x64
```

Deploy the MSIX package from your IDE (Rider or Visual Studio's "(Package)" launch profile), then reload Command Palette.

## License

[MIT](LICENSE)
