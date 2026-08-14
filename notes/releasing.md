# Releasing

Skeleton — modeled on MarketExtension's `notes/releasing.md`; fill in as the release
infrastructure lands (see `notes/store-readiness.md` for the full checklist). Current version:
**0.1.0.0** (pre-release).

## Version bump — all sites together, one dedicated commit

Six sites today (D14 in the checklist will consolidate the three UserAgent constants into one,
shrinking this table):

| File | Field |
|---|---|
| `AgentsPanelExtension/AgentsPanelExtension.csproj` | `<AppxPackageVersion>` |
| `AgentsPanelExtension/Package.appxmanifest` | `Identity Version=` |
| `AgentsPanelExtension/app.manifest` | `assemblyIdentity version=` |
| `AgentsPanelExtension/Data/Claude/ClaudeUsageProvider.cs` | `UserAgent` (`agents-panel/<ver>`, 3-part) |
| `AgentsPanelExtension/Data/Codex/CodexUsageProvider.cs` | `UserAgent` |
| `AgentsPanelExtension/Data/Copilot/CopilotUsageProvider.cs` | `UserAgent` |

MSIX wants 4-part `Major.Minor.Build.Revision`; the UA strings carry the 3-part form. Bump
one-liner (adjust the UA separately — different format):

```powershell
$files = @("AgentsPanelExtension/AgentsPanelExtension.csproj",
           "AgentsPanelExtension/Package.appxmanifest",
           "AgentsPanelExtension/app.manifest")
$files | ForEach-Object { (Get-Content $_) -replace '0\.1\.0\.0','<NEW>' | Set-Content $_ }
```

Also update the "Current version" line at the top of this file. Commit message = the version
(e.g. `0.2.0.0`). Never amend.

## Release (once E18/E19 land)

⚠️ **User-only step** — agents never run `gh workflow run` (or commit) on their own; see AGENTS.md.

```
gh workflow run release-msix.yml --ref main -f release_notes="…"
```

Reads the version from the csproj, builds x64 + ARM64, bundles with makeappx, signs with the
self-signed cert (secrets `SIGNING_CERT_PFX` + `SIGNING_CERT_PASSWORD`), creates the GitHub
Release tagged `v<version>` (tag is created by the workflow — don't tag by hand).

## Partner Center

Download the `.msixbundle` from the GitHub Release → Partner Center → app → new submission →
Packages → upload → update description/notes → submit. The Store re-signs the package. For WinGet,
download the **Store-signed** bundle back from Partner Center and re-upload it to the same GitHub
Release (self-signed fails WinGet's sandbox validation) — then run `update-winget.yml`.

## Local build caveats

- Always `dotnet build AgentsPanelExtension.sln -p:Platform=x64` — without the flag MSBuild picks
  ARM64 (alphabetically first in the sln) and the package won't deploy on this x64 machine.
- Agents build only; deployment happens from Rider (see AGENTS.md — never `Add-AppxPackage`).
