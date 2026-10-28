# RAM behavior study

Reviewed upstream commit `c2b13d146d5db609d499870145e8d2dff2506581` as a behavioral reference. Reference checkout is ignored under `artifacts/reference/ram`; no source is linked into Hive.

## Adding and opening accounts

[AccountBrowser.cs](https://github.com/ic3w0lf22/Roblox-Account-Manager/blob/c2b13d146d5db609d499870145e8d2dff2506581/RBX%20Alt%20Manager/Classes/AccountBrowser.cs) launches Chromium/Puppeteer, navigates to login, observes completed authentication, extracts the security cookie and invokes account addition. It can also seed cookies when opening an existing account. The legacy implementation includes request-body password capture and browser configuration/script hooks.

[AccountManager.cs](https://github.com/ic3w0lf22/Roblox-Account-Manager/blob/c2b13d146d5db609d499870145e8d2dff2506581/RBX%20Alt%20Manager/AccountManager.cs) deduplicates by Roblox user ID, persists account metadata/secrets, supplies grouping and aliases, and orchestrates launch batches and periodic maintenance. The old on-disk file can be raw JSON, machine DPAPI data or password-encrypted binary despite its `.json` name.

Hive recreates browser sign-in, stable-ID deduplication and saved-session browsing with isolated contexts, explicit ownership validation and DPAPI CurrentUser storage. Website passwords are not intercepted. Raw RAM imports get a metadata-only preview and a transactional write; encrypted RAM files must be exported/decrypted by the original application first.

## Launching and process identity

[Account.cs](https://github.com/ic3w0lf22/Roblox-Account-Manager/blob/c2b13d146d5db609d499870145e8d2dff2506581/RBX%20Alt%20Manager/Classes/Account.cs) obtains CSRF/authentication tickets, builds game launch requests, handles PlaceId/JobId and private servers, and associates launches with browser tracker identifiers. The main form controls launch order/delay. [RobloxWatcher.cs](https://github.com/ic3w0lf22/Roblox-Account-Manager/blob/c2b13d146d5db609d499870145e8d2dff2506581/RBX%20Alt%20Manager/Classes/RobloxWatcher.cs) and RobloxProcess.cs inspect processes/logs and maintain window/process state.

Hive obtains authentication tickets with one CSRF retry and builds encoded PlaceId/JobId protocol requests. A bounded serial queue spaces launches; the Windows dispatcher matches the generated browser tracker identifier against Roblox process command lines before assigning AccountId to PID. An unconfirmed launch stays AwaitingProcess. Explicit manual linking remains available, and process start time rejects recycled PIDs. Browser-open state is never treated as a running game.

## Feature disposition

| Legacy capability | Hive status |
| --- | --- |
| Manual login / User:Pass / cookies / Custom URL + JS | Implemented with isolated browser contexts and identity verification; user-supplied scripts execute only when explicitly selected |
| Open an account's browser | Implemented with private context restoration |
| Aliases/descriptions and RAM import | Descriptions editable; imported alias retained; readable JSON import implemented |
| Refresh cookies | Implemented with scheduling, CSRF retry, encrypted replacement and rate-limit throttling |
| Groups / presence | Groups schema exists; group UI and presence remain future work |
| PlaceId/JobId, private servers, follow user | PlaceId/JobId ticket launcher implemented; private servers and follow-user remain future work |
| Launch queue, multi-instance, watcher, rejoin | Bounded queue and correlated process watcher implemented; multi-instance and automatic rejoin remain future work |
| Account utilities | Password/email/privacy mutations were studied; not enabled in this increment |
| Local API / account control | Not adopted; Hive has no listening web server or custom backend |
| Custom script/captcha-service hooks | Explicit Custom URL + JS flow implemented; external captcha-service integration not adopted |

## Current API reference

[Roblox Accounts documentation](https://create.roblox.com/docs/cloud/reference/features/accounts) lists the authenticated-user and session-refresh APIs. Hive performs no password changes. Session renewal is not recovery of a revoked credential: when interactive verification is required, the UI asks for sign-in.
