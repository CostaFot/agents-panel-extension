# Agents Panel Extension for Command Palette — Claude Guide

A PowerToys **Command Palette** extension. .NET / C# / MSIX. (Exact purpose/features TBD — to be filled
in once the project is defined.)

## Reference project — use it A LOT

This extension is scaffolded from **MarketExtension**, a prior CmdPal extension by the same author:

- **Location:** `C:\Users\jarla\code\MarketExtension`
- **When building anything here, look there first** — copy/adapt its patterns rather than reinventing:
  project layout, `.csproj` / `Directory.Build.props` / packaging setup, `Package.appxmanifest`,
  commands provider entry point, pages, commands, settings manager, `ProcessHelper`, `Log`, icons,
  resx string handling.
- Its `CLAUDE.md` and `notes/` folder (architecture, cmdpal-toolkit gotchas, releasing) document
  hard-won CmdPal knowledge — read `notes/cmdpal-toolkit.md` before fighting any toolkit behavior.
- A second, older extension (AdbExtension) also exists as reference material inside
  `MarketExtension/reference/`.

## Documentation links

- [Extension overview & concepts](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/extension-development)
- [Creating an extension](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/creating-an-extension)
- [Toolkit namespace — full class list](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/microsoft-commandpalette-extensions-toolkit/microsoft-commandpalette-extensions-toolkit)
- [Command results](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/command-results)

## Project conventions (inherited from MarketExtension)

- New commands → `Commands/`, extend `InvokableCommand`.
- New pages → `Pages/`, extend `ListPage`, `DynamicListPage`, or `ContentPage`.
- External process execution goes through a `ProcessHelper` — never use `Process` directly in
  command files.
- Error toasts use `CommandResult.KeepOpen()` so the user can read them; one-shot success toasts use
  the default `Dismiss()`.
- Don't hardcode user-facing strings — use `Properties/Resources.resx`.
- Logging via `Log.Info/Warn/Error(tag, message)`, all `[Conditional("DEBUG")]` — Release ships silent.
- **Fail loud:** crash on genuinely-wrong states; don't swallow errors and silently degrade.

## Known CmdPal gotchas (from MarketExtension — details in its `notes/cmdpal-toolkit.md`)

- ⚠️ **The Write tool drops Segoe MDL2 glyph chars** (private-use code points save as blank). Use C#
  Unicode escapes (`\uXXXX`) or `((char)0xE9F9)`, and byte-check after editing.
- ⚠️ **Page-activation hook:** `GetItems()` is called before `ItemsChanged` is subscribed, so a
  constructor `RaiseItemsChanged` is lost — re-implement `INotifyItemsChanged` and refresh from the
  `add` accessor.
- ⚠️ **ContentPage with a single `FormContent`** auto-focuses the card's first action, stealing Enter.

## Git

- Never amend commits (`git commit --amend`) — always create a new commit, unless explicitly asked to
  amend in the moment.
