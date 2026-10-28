# Hive contributor instructions

- Keep the UI in English, compact and focused on account-manager workflows.
- Use RAM as a behavioral reference; keep Hive modular and do not transplant its monolithic architecture.
- Store secrets locally through DPAPI. Never log passwords, cookies, authentication tickets or protocol URLs. No telemetry or custom remote backend.
- Use local SVG icons and respect the app and Windows animation preferences.
- Publish verified changes to `main`, as requested by the owner. Use GitHub CLI for commit/publish workflow; do not force-push.
- Run `dotnet run --project tests/Hive.SmokeTests -c Release --artifacts-path artifacts/current` and build the solution before publication.
- Keep generated artifacts, browser downloads and reference repositories under ignored paths. Preserve the upstream AGPL-3.0 license.
