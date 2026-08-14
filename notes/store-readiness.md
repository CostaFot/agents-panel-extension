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
- [x] **A2 [agent] Real `provider_copilot.png`.** (done 2026-08-14 — decision: NO real third-party
  branding for any provider icon, generic tiles only. New 64×64 purple rounded tile with white
  `{ }` braces glyph, matching the Claude "C" / Codex ">_" tile system; 997 bytes.)
- [ ] **A3 [agent] Downsize oversized icons.** `agentspanel_logo_base_square.png` (1.17 MB) loads
  for 16–32 px rows — package bloat. (After A1 lands; `provider_copilot.png` resolved by A2.)
- [ ] **A4 [user] Partner Center name reservation.** Reserve "Agents Panel for Command Palette";
  confirm the assigned `Identity Name` matches `CostaFotiadis.AgentsPanelforCommandPalette` in
  `Package.appxmanifest` + csproj `AppxPackageIdentityName`. Publisher CN already matches.

## B. Certification blockers — content & legal

- [x] **B5 [agent] Hosted privacy policy + terms.** (docs/ + deploy-pages.yml written 2026-08-14;
  GitHub Pages enabled + deployed 2026-08-14) Partner Center requires a privacy-policy URL.
  Create `docs/{index,privacy,terms}.html` + `style.css` modeled on MarketExtension's `docs/`,
  adapted to this app: credentials read locally from other apps' stores, requests go directly to
  Anthropic/OpenAI/GitHub, zero telemetry, Copilot PAT stored plaintext in settings JSON.
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
- [x] **D16 [agent] Untrack `.idea/`.** (verified 2026-08-14: stale item — `git ls-files` shows
  nothing under `.idea/` is tracked; nothing to do.)
- [x] **D17 [user] Merge `scaffold` → `main`.** (done — "Initial scaffold (#1)" merged to main;
  `scaffold`'s remote is gone. Current work continues on `working_through_release_checklist`,
  which will PR to main the same way.)

## E. Release infrastructure (copy from MarketExtension, rename)

- [x] **E18 [user] Signing cert + secrets.** (done 2026-08-14: script at
  `AgentsPanelExtension/create-signing-cert.ps1`, cert "Agents Panel Extension Signing" generated
  with the manifest CN — expires **2027-08-14**, re-run then — both secrets set on the repo, pfx
  git-ignored. User to back up signing.pfx + password.) Still later, for WinGet: `WINGET_TOKEN`
  (classic PAT, public_repo).
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

- [x] **G24 [agent] Tests.** (decided 2026-08-14: **won't do** — user call, no test project.)
- [x] **G25 [agent] Copilot PAT plaintext.** (decided 2026-08-14: **won't do** the setting-description
  note — user call. The privacy page (B5) already discloses the plaintext storage, which covers the
  formal side.)
- [x] **G26 [agent] Settings wording.** (done 2026-08-14) Descriptions made provider-neutral:
  "Show model-specific limits (e.g. Opus, Sonnet) as their own rows" / "Show pay-as-you-go extra
  usage when the account reports it" (the old "credits balance" was wrong for Copilot's "n used"
  row).
- [x] **G27 [agent] HttpClient timeout.** (decided 2026-08-14: **won't do.** Verified the 8s
  `MaxDelay` only caps retry waits, NOT request wall time — the 100s HttpClient default applies per
  attempt. Accepted: MarketExtension ships the same default with no issues, the UI never blocks,
  and keep-last-good + stale text absorb a slow fetch. Trivial retrofit if ever reported.)
- [x] **G28 [agent] Delete dead `Assets/LockScreenLogo.scale-200.png`** (resolved 2026-08-14:
  **kept**, user decision — matching MarketExtension, which shipped the byte-identical file through
  Store certification. For the record: it is not actually a valid PNG — its leading `0x89` was
  text-mode-corrupted to `EF BF BD` somewhere in MarketExtension's early history — but nothing
  references or parses it, so it rides along inert.)

---

## Ship order (proven MarketExtension pipeline)

1. Bump versions (see `notes/releasing.md` table) — one dedicated commit.
2. `gh workflow run release-msix.yml --ref main -f release_notes="…"` → self-signed msixbundle +
   GitHub Release.
3. Partner Center: upload the bundle, submit (Store re-signs it).
4. Download the Store-signed bundle from Partner Center → re-upload to the same GitHub Release.
5. `update-winget.yml` (or manual `wingetcreate` the first time) pointing at the Store-signed
   bundle.
