# Hive

A local-first Roblox account manager built from scratch on .NET 10, WPF and MVVM.

## Run

Requires Windows and .NET SDK 10.0.203 or a later compatible patch.

```powershell
dotnet build Hive.slnx -c Release --artifacts-path artifacts/build
dotnet run --project src/Hive.App -c Release --artifacts-path artifacts/build
```

A ready framework-dependent build is published to `artifacts/Hive/Hive.App.exe`. Close older Hive builds before switching versions. All builds use the same local database.

## Account workflows

- **Add account** offers Manual Login, User:Pass (single or newline-separated), Cookie(s), and Custom URL + JS. Manual Login opens the Roblox site immediately without an intermediate form. User:Pass fills the fields and leaves the final Log In click and verification to the user. After normal Roblox sign-in and any two-step verification, Hive discovers the authenticated username and stable Roblox user ID, deduplicates the account, and encrypts the session locally. It does not intercept passwords typed into the website.
- **Open browser** verifies the selected account and restores its encrypted cookies into an isolated private browser context. Changed browser cookies are checked against the expected account before storage. Windows close when Hive exits.
- **Edit** or a row double-click opens a separate details dialog for the description and optional password. There is no permanent credential inspector. Leaving the password field empty preserves the stored password. Passwords are masked by default; Settings > Accounts can reveal them. Imported aliases are preserved as account metadata.
- **Session** shows validation state, allows explicit reauthentication or cookie import, and provides manual account-to-PID assignment.
- **Import RAM** previews a readable/decrypted `AccountData.json` account array. It imports username, alias, description, password and cookie. Existing usernames are skipped; the batch is transactional. Imported sessions remain unverified until checked. The importer does not decrypt RAM's machine/password encryption formats or import arbitrary scripts/settings.

## Workspace and utilities

Groups are stored in SQLite and can be created, filtered, assigned to selected accounts and removed without deleting accounts. Favorites, saved places and the current Place/Job IDs are stored in atomic local preferences. Ctrl/Shift selection launches multiple accounts through the bounded queue. All accounts resets group filters; newly added accounts are made visible immediately.

Utilities contains RAM import, session checking, an in-memory Network Log (250 records), metadata export, a consistent SQLite backup including DPAPI-encrypted secret payloads, and graceful closure requests for selected linked games. Network Log records operation labels, Roblox service host, HTTP status and duration; it never captures headers, bodies, query strings, cookies, account names or tickets. A copied encrypted backup remains bound to the Windows user that encrypted it.

Hive opens at 900 × 560, the browser at 820 × 600, and compact game windows at 800 × 500. Compact game sizing can be disabled in Preferences > Launch. Browser navigation reuses one managed private page and closes Chromium's initial blank page. Launcher correlation understands both native `-b` arguments and protocol URIs forwarded by a registered launcher.

## Status and automatic maintenance

The first two table columns are green only when a live `RobloxPlayerBeta` process is explicitly linked to that account. Process name, PID and start time are checked every two seconds; process exit and PID reuse remove the association. Red means a confirmed expired/rejected session, failed decryption, or an identity mismatch. Valid sessions without a linked game process remain neutral. Roblox presence and arbitrary already-open game windows are not guessed to belong to an account. The Place ID / optional Job ID launcher obtains an authentication ticket with a CSRF retry, dispatches the Roblox protocol and correlates the browser tracker ID with the actual process command line. Launches pass through a bounded serial queue with spacing. If identity cannot be confirmed, the state stays AwaitingProcess and never turns green automatically. This does not bypass the Roblox client single-instance policy.

Automatic session checks run every five minutes while Hive is open. Session renewal uses Roblox's documented `/v2/session/refresh` endpoint when a valid session is at least 24 hours old, or near expiry and at least one hour old. CSRF challenges get one retry. Failed renewal attempts are throttled for one hour. Server-issued replacements are encrypted immediately. Network failures, 403 challenges and rate limits do not mark an account lost. Session checking pauses for five minutes after a 429, cookie batches pause at rate limits, and sign-in does not validate an unchanged cookie more than once every 30 seconds. A revoked session still needs interactive sign-in; Hive never changes a Roblox password automatically.

## Interface and preferences

The interface is English. Light and Dusk surfaces, Honey/Iris/Ocean accents, compact rows, password visibility, automatic checking/renewal, compact game windows and Place/Job ID are saved locally. Button feedback and opening transitions respect both Hive's animation switch and Windows animation settings. Icons are local SVG assets rendered as vectors; Lucide SVG icons are bundled with their license; no emoji icon font or remote asset service is used. Preferences has Appearance, Accounts and Launch sections with stable navigation order.

Install the PuppeteerSharp-pinned Chrome for Testing build with:

```powershell
dotnet run --project tools/Hive.BrowserSetup -c Release
```

The setup uses PuppeteerSharp BrowserFetcher and checks a real launch through StealthPlugin, private-context navigation and cookie roundtrip before selecting the executable automatically. Browser selection is managed automatically; there is no local executable picker in Preferences. Browser sign-in is a separate managed Chromium window, not an embedded WPF web view. Private contexts keep website session cookies separate from the persistent browser metadata directory. Hive automatically downloads the PuppeteerSharp-pinned browser when no configured/managed/installed browser is available. Portable builds keep it in `Browser/` beside the executable when writable, with local-app-data fallback. Setup also accepts `--path <folder>` and `--roblox` to verify the real sign-in page. A managed manifest is written only after a successful launch.

The published PuppeteerExtraSharp 3.1.1 binary is incompatible with PuppeteerSharp 25's API. Hive compiles the MIT-licensed Stealth subset from source against its current PuppeteerSharp and awaits page initialization before navigation; see the vendor README for provenance and changes. The browser launch, isolated cookies and real Roblox sign-in-page navigation are verified separately from account authentication. Real account sign-in still requires the user's Roblox verification.

Account-entry dialogs stay open during sign-in, show progress/errors and support cancellation and retry. They close only after a verified account has been saved; no phantom account is created when browser startup fails.

## Storage and trust

Data lives in `%LOCALAPPDATA%/Hive/`: `hive.db`, `preferences.json`, `BrowserProfiles/` and `Logs/`. SQLite uses WAL, foreign keys and transactional versioned migrations. Account metadata is plaintext; password and cookie payloads use Windows DPAPI CurrentUser. Source RAM exports remain the user's files; Hive does not rewrite them. Diagnostic logs roll daily with seven files retained and omit account fields, cookies and exception messages. No telemetry or Hive backend is included; account operations contact Roblox directly.

## Modules

- `Hive.App`: WPF views, local SVG rendering, themes, settings, view model, composition root.
- `Hive.Core`: domain records and storage/provider contracts.
- `Hive.Storage`: SQLite + Dapper repositories, migrations, encrypted credentials/sessions, transactional RAM import.
- `Hive.Infrastructure`: DPAPI, launch queue, Windows protocol dispatch and account/process tracking.
- `Hive.Browser`: PuppeteerSharp 25.15.0 with a source-compatible MIT StealthPlugin subset and isolated contexts.
- `Hive.Roblox`: account identity onboarding, session validation/renewal and authenticated launch tickets.

See [RAM behavior analysis](docs/RAM-behavior.md) for the old manager's workflow map and the implementation boundary. No RAM implementation source is compiled into Hive.

## Verification

```powershell
dotnet run --project tests/Hive.SmokeTests -c Release --artifacts-path artifacts/build
```

The Windows smoke runner checks migrations, CRUD, case-insensitive uniqueness, search/filters, DPAPI and password masking, session identity/rotation/renewal, CSRF retry and transient errors, PID assignment/exit/reuse, browser account deduplication, transactional RAM import and rollback. Automated checks use simulated network responses and isolated temporary databases. An independent subprocess verifies accounts, encrypted secrets, groups, favorites and places after reopening; a separate BrowserSetup check verifies real Chromium, one-page navigation and native browser bounds. It also checks ticket negotiation, launch queue deduplication, protocol encoding and unconfirmed process states. It renders the account-entry dialogs, details dialog, animation clocks, the main view at two sizes, all settings categories, Light/Dusk themes and the import preview; WPF binding errors fail the run. Preview accounts exist only in the test process.

Remaining roadmap: managed browser profile library, configurable launch concurrency, multi-instance support, private-server/follow-user launching and watcher-based auto-rejoin with bounded backoff.
