# Store readiness checklist

The ordered path from working prototype to Microsoft Store — plus GitHub Releases and WinGet,
following MarketExtension's proven pipeline (Store ID 9MV7M639533Q; see its `notes/releasing.md`,
`notes/winget-publishing.md`, `notes/store-listing.md` for the source playbook).

Tags: **[user]** = only the developer can do it (Partner Center, art, certs, secrets, Rider).
**[agent]** = a coding agent can do it end-to-end (verified by `dotnet build … -p:Platform=x64`).
**[either]** = drafting work either can start.

Decisions already made (2026-08-14): **no demo mode** (remove the README claim; certification test
notes carry that weight instead); ship the **full triple channel** (Store + GitHub Releases +
WinGet).

Already verified done: new COM GUID (`90ff65ac-91f0-4e40-b50c-4dfa6b58c511`), real Partner Center
publisher (`CN=3D57AA92-97A9-42D2-8CB0-4207D9145514`) in manifest + csproj, resx/Designer in sync,
manifest description written, minimal capabilities, GitHub remote configured.

---

## A. Certification blockers — identity & branding

- [ ] **A1 [user] Real app logo + MSIX asset regen.** All 55 tile/store/splash PNGs in
  `AgentsPanelExtension/Assets/` are byte-copies of MarketExtension's `$ >_` branding, and
  `Assets/agentspanel_logo_base_square.png` is its source art renamed. Design a new square source
  PNG → VS Manifest Designer → Visual Assets with **"Apply recommended padding" OFF**
  (already-padded art double-insets) → regenerate every scale/targetsize variant. The same square
  source is the in-app icon (`AgentsPanelCommandsProvider.cs`, `UsagePage.cs`, `LegalPage.cs`,
  `ProviderIcons.cs` fallback) — two separate icon systems, one source image.
- [ ] **A2 [user] Real `provider_copilot.png`.** Currently byte-identical to the app logo
  (1289×1289, 1.17 MB). Needs a purpose-made 64×64 mark like `provider_claude.png` /
  `provider_codex.png`.
- [ ] **A3 [agent] Downsize oversized icons.** `agentspanel_logo_base_square.png` (1.17 MB) and
  `provider_copilot.png` load for 16–32 px rows — ~2.3 MB of package bloat. (After A1/A2 land.)
- [ ] **A4 [user] Partner Center name reservation.** Reserve "Agents Panel for Command Palette";
  confirm the assigned `Identity Name` matches `CostaFotiadis.AgentsPanelforCommandPalette` in
  `Package.appxmanifest` + csproj `AppxPackageIdentityName`. Publisher CN already matches.

## B. Certification blockers — content & legal

- [x] **B5 [agent] Hosted privacy policy + terms.** (docs/ + deploy-pages.yml written 2026-08-14; **[user] still to do: enable GitHub Pages on the repo** — Settings → Pages → Source: GitHub Actions) Partner Center requires a privacy-policy URL.
  Create `docs/{index,privacy,terms}.html` + `style.css` modeled on MarketExtension's `docs/`,
  adapted to this app: credentials read locally from other apps' stores, requests go directly to
  Anthropic/OpenAI/GitHub, zero telemetry, Copilot PAT stored plaintext in settings JSON. Copy
  `deploy-pages.yml`; **[user]** enables GitHub Pages on the repo.
- [x] **B6 [agent] LICENSE.** Add MIT (matching MarketExtension). Without it the public repo is
  all-rights-reserved by default.
- [x] **B7 [agent] README fixes.** (demo-mode claim removed, build/deploy section fixed 2026-08-14; badges/screenshots deferred to post-approval) Remove the false "Demo mode" claim (line 24 — decided: not
  implementing it); fix "Deploy the MSIX from Visual Studio" (line 52) vs the Rider reality. Later
  (after Store approval): release/downloads badges, Store badge
  (`https://apps.microsoft.com/detail/<store-id>`), winget one-liner, screenshots — copy
  MarketExtension's README structure.
- [x] **B8 [either] Store listing copy → `notes/store-listing.md`.** (drafted 2026-08-14 — user reviews before Partner Center paste) Short + full description,
  **non-affiliation paragraph** (not affiliated with/endorsed by Anthropic, OpenAI, GitHub, or
  Microsoft), disclose that the app requires third-party subscriptions/sign-ins (Store policy),
  disclaimer that data comes from undocumented endpoints and may stop working or be inaccurate.
  Never overclaim: no "official", no "real-time". Trademark use nominative only — extra care since
  all three endpoints are ToS-gray.
- [x] **B9 [either] Certification test notes.** (drafted 2026-08-14 → `notes/certification-notes.md`) A Store reviewer has no Claude/Codex/Copilot
  credentials, so they'll see three "Sign in" rows. Write submission notes explaining what the app
  does, why those rows appear, and how to exercise the UI (e.g. the Copilot PAT setting with a
  fine-grained test token, if feasible). C10 makes those rows self-explanatory, which helps here.

## C. First-run / UX gaps

- [x] **C10 [agent] Actionable NotConfigured rows.** (resolved 2026-08-14 — decision: rows stay
  deliberately non-actionable; no deep-links or third-party sign-in guidance, the app never handles
  or advises on the agents' own apps. Wording made neutral/factual instead: "Not signed in to {0}" /
  "No sign-in found on this PC", TokenExpired subtitle likewise de-guided.)
- [x] **C11 [agent] Per-provider enable/disable.** (decided 2026-08-14: **won't do.** Hub showing
  all providers — including "Not signed in" rows for unused ones — is acceptable; and the dock
  already gives full per-provider control via the host's own pin/unpin per band (one band per
  provider), which was the deliberate design. `IsAvailable` stays a dormant seam.)
- [x] **C12 [agent] Fix misleading empty dock band.** (fixed 2026-08-14) Zero-window fall-through
  split per status: Ok-but-empty → "No usage data" / "The account reported no active limits";
  RateLimited → "Rate-limited" / rate-limit subtitle; Error keeps "Can't reach {0}". Dock wording
  also made neutral per the C10 decision ("Not signed in"; expired subtitle no longer tells the
  user to go use the agent — behavior "can change", so no promises).
- [x] **C13 [agent] Hardcoded strings → resx.** (done 2026-08-14) `DirectLaunch_Message` (format
  string over `Extension_DisplayName`/`Command_AgentsPanel`; caption reuses `Extension_DisplayName`)
  + `Legal_Markdown` moved to resx, Designer.cs in lock-step. No wording changes.

## D. Versioning & build hygiene

- [x] **D14 [agent] Version/UA consolidation.** (done 2026-08-14) UA now assembly-derived in
  `Helpers/AppInfo.cs` (csproj `<Version>` mirrors `<AppxPackageVersion>`); the three per-provider
  constants deleted. Bump sites down to three manifest files — `notes/releasing.md` table updated
  and verified (built dll carries 0.1.0.0 → UA "agents-panel/0.1.0", byte-identical to the old
  literal).
- [x] **D15 [agent] csproj/sln cleanup.** (done 2026-08-14) `<Company>/<Product>/<Copyright>`
  added (verified in the built dll); `PrepareAssets` comment fixed. x86 sln configs KEPT by user
  decision (removal reverted). Preview pins (`WindowsSdkPackageVersion 10.0.26100.68-preview`,
  NetAnalyzers) deliberately kept — MarketExtension shipped to the Store on the same pin; revisit
  post-approval. The ARM64-first alphabetical trap remains — `-p:Platform=x64` stays mandatory.
- [ ] **D16 [agent] Untrack `.idea/`.** Ignored in `.gitignore` but committed earlier, so it's
  still tracked.
- [ ] **D17 [user] Merge `scaffold` → `main`.** All work is on `scaffold`; `origin/main` is one
  empty initial commit — the public repo shows nothing.

## E. Release infrastructure (copy from MarketExtension, rename)

- [ ] **E18 [user] Signing cert + secrets.** Copy `create-signing-cert.ps1` (CN already correct),
  run as admin, set GitHub secrets `SIGNING_CERT_PFX` (base64) + `SIGNING_CERT_PASSWORD`.
  `.gitignore` already covers `*.pfx`. Later, for WinGet: `WINGET_TOKEN` (classic PAT,
  public_repo).
- [x] **E19 [agent] Workflows.** (build-check/release-msix/deploy-pages written 2026-08-14; release-extension.yml + Inno Setup deferred as noted) Copy into `.github/workflows/` and rename env vars
  (`DISPLAY_NAME`/`EXTENSION_NAME`/`FOLDER_NAME`): `build-check.yml` (PR gate),
  `release-msix.yml` (x64+ARM64 → makeappx bundle → signtool → GitHub Release),
  `deploy-pages.yml`. Optional later: `release-extension.yml` + `build-exe.ps1` +
  `setup-template.iss` (its `[Registry]` COM block must use this repo's GUID
  `90ff65ac-91f0-4e40-b50c-4dfa6b58c511`).
- [x] **E20 [agent] `notes/releasing.md`.** Skeleton created alongside this checklist.
- [ ] **E21 [user] Screenshots → `listing/`.** Hub, provider page, dock bands, settings — doubles
  as Store listing shots and README art. Needs A1/A2 branding first.

## F. Store submission & post-Store

- [ ] **F22 [user] Submit to Partner Center.** Run `release-msix.yml` → download the
  `.msixbundle` from the GitHub Release → Partner Center → new submission → upload → listing copy
  (B8) + screenshots (E21) + privacy URL (B5) + certification notes (B9) → submit. `runFullTrust`
  needs its standard justification (CmdPal COM extension-host requirement).
- [ ] **F23 [user] WinGet.** After Store approval only. Package ID
  `CostaFotiadis.AgentsPanelForCommandPalette` (28 chars — fits the 32-char/segment limit, no
  shortening needed unlike Markets). Must point at the **Store-signed** bundle downloaded from
  Partner Center and re-uploaded to the GitHub Release — the self-signed CI bundle fails WinGet's
  sandbox validation. The locale yaml's `Tags:` must include `windows-commandpalette-extension`
  (enables CmdPal's one-click install). First submission manual via `wingetcreate new` → hand-edit
  → `winget validate` → `wingetcreate submit`; afterwards copy `update-winget.yml` **[agent]** for
  repeat submissions.

## G. Recommended, not blocking

- [ ] **G24 [agent] Tests.** Test project over the pure high-risk logic:
  `CopilotUsageProvider.MapWindows` (3 payload generations), `UsageRepository.Merge`
  (keep-last-good), `UsageSettingsManager.RefreshMinutes` clamp, reset-time parsing — with
  captured sample JSON from the three undocumented endpoints as a regression net for when they
  reshape.
- [ ] **G25 [agent] Copilot PAT plaintext.** Note it in the setting description + privacy page
  (CmdPal TextSetting has no masking).
- [ ] **G26 [agent] Settings wording.** `showModelWindows`/`showExtraUsage` descriptions are
  Claude-worded but filter every provider — reword.
- [ ] **G27 [agent] HttpClient timeout.** Verify `HttpRetry`'s 8s bail actually caps wall time;
  consider an explicit `Timeout` (default is 100s).
- [ ] **G28 [agent] Delete dead `Assets/LockScreenLogo.scale-200.png`** (unreferenced VS template
  leftover).

---

## Ship order (proven MarketExtension pipeline)

1. Bump versions (see `notes/releasing.md` table) — one dedicated commit.
2. `gh workflow run release-msix.yml --ref main -f release_notes="…"` → self-signed msixbundle +
   GitHub Release.
3. Partner Center: upload the bundle, submit (Store re-signs it).
4. Download the Store-signed bundle from Partner Center → re-upload to the same GitHub Release.
5. `update-winget.yml` (or manual `wingetcreate` the first time) pointing at the Store-signed
   bundle.
