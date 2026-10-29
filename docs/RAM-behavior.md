# RAM behavior study

Reviewed upstream commit `c2b13d146d5db609d499870145e8d2dff2506581` as a behavioral reference. Reference checkout is ignored under `artifacts/reference/ram`; no source is linked into Hive.

## Adding and opening accounts

[AccountBrowser.cs](https://github.com/ic3w0lf22/Roblox-Account-Manager/blob/c2b13d146d5db609d499870145e8d2dff2506581/RBX%20Alt%20Manager/Classes/AccountBrowser.cs) launches Chromium/Puppeteer, navigates to login, observes completed authentication, extracts the security cookie and invokes account addition. It can also seed cookies when opening an existing account. The legacy implementation includes request-body password capture and browser configuration/script hooks.

[AccountManager.cs](https://github.com/ic3w0lf22/Roblox-Account-Manager/blob/c2b13d146d5db609d499870145e8d2dff2506581/RBX%20Alt%20Manager/AccountManager.cs) deduplicates by Roblox user ID, persists account metadata/secrets, supplies grouping and aliases, and orchestrates launch batches and periodic maintenance. The old on-disk file can be raw JSON, machine DPAPI data or password-encrypted binary despite its `.json` name.

Hive recreates browser sign-in, stable-ID deduplication and saved-session browsing with isolated contexts, explicit ownership validation and DPAPI CurrentUser storage. Website passwords are not intercepted. Raw RAM imports get a metadata-only preview and a transactional write; encrypted RAM files must be exported/decrypted by the original application first.

## Launching and process identity

[Account.cs](https://github.com/ic3w0lf22/Roblox-Account-Manager/blob/c2b13d146d5db609d499870145e8d2dff2506581/RBX%20Alt%20Manager/Classes/Account.cs) obtains CSRF/authentication tickets, builds game launch requests, handles PlaceId/JobId and private servers, and associates launches with browser tracker identifiers. The main form controls launch order/delay. [RobloxWatcher.cs](https://github.com/ic3w0lf22/Roblox-Account-Manager/blob/c2b13d146d5db609d499870145e8d2dff2506581/RBX%20Alt%20Manager/Classes/RobloxWatcher.cs) and RobloxProcess.cs inspect processes/logs and maintain window/process state.

Hive obtains authentication tickets with one CSRF retry and builds encoded PlaceId/JobId protocol requests. A bounded queue with configurable parallelism spaces launches; the Windows dispatcher matches the generated browser tracker identifier against Roblox process command lines before assigning AccountId to PID. An unconfirmed launch stays AwaitingProcess. Explicit manual linking remains available, and process start time rejects recycled PIDs. Browser-open state is never treated as a running game.

## Feature disposition

| Legacy capability | Hive status |
| --- | --- |
| Manual login / User:Pass / cookies / Custom URL + JS | Implemented with isolated browser contexts and identity verification; user-supplied scripts execute only when explicitly selected |
| Open an account's browser / profiles | Private context restoration, per-account profile library, rename/reset/archive and last-used metadata implemented |
| Aliases/descriptions and RAM import | Descriptions editable; imported alias retained; readable JSON import implemented |
| Refresh cookies | Implemented with scheduling, CSRF retry, encrypted replacement and rate-limit throttling |
| Groups / favorites / saved places | SQLite group management, favorite filter, saved places and current launch target implemented; Roblox presence remains future work |
| PlaceId/JobId, private servers, follow user | PlaceId/JobId, private invite/access code/share-link resolution and follow-user ID protocol targets implemented; live permissions remain enforced by Roblox |
| Launch queue, multi-instance, watcher, rejoin | Configurable parallel queue, cancel controls, correlated process watcher, pre-launch singleton coordination and bounded exponential auto-rejoin implemented; process-alive disconnect detection remains future work |
| Account utilities | Session checks, encrypted database backup, metadata export, redacted Network Log and graceful selected-game closure implemented; official account/security/privacy browser pages added; direct password/email mutations remain interactive |
| Local API / account control | Not adopted; Hive has no listening web server or custom backend |
| Custom script/captcha-service hooks | Explicit Custom URL + JS flow implemented; external captcha-service integration not adopted |

## Current API reference

[Roblox Accounts documentation](https://create.roblox.com/docs/cloud/reference/features/accounts) lists the authenticated-user and session-refresh APIs. Hive performs no password changes. Session renewal is not recovery of a revoked credential: when interactive verification is required, the UI asks for sign-in.
