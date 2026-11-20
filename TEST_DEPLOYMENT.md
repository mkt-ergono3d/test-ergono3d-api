# API test repository snapshot

This is a source snapshot for isolated deployment testing. It excludes the original Git history, build artifacts, local tooling state and real credentials. Original local files were not modified.

## Required test configuration

- Replace the placeholders in appsettings.json and appsettings.Development.json, or supply ASP.NET Core environment overrides: ConnectionStrings__Primary and Jwt__Key. Configure the existing Stripe-api-key and Stripe-webhook-key configuration keys through a private deployment configuration or secret store.
- Use a separate test database and Stripe test credentials/price IDs. Do not reuse the production signing key or database.
- Set INSOLE_AI_API_KEY and INSOLE_SMTP_PASSWORD for the application if those features are required.
- The optional backup utility reads PGPASSWORD.
- The legacy SSH deployment helper reads INSOLE_DEPLOY_INT_HOST, INSOLE_DEPLOY_INT_USER, INSOLE_DEPLOY_INT_PASSWORD and the equivalent INSOLE_DEPLOY_PROD_* variables.
- deploy/int.yaml requires PRIMARY_DB. Confirm its relationship to the application's ConnectionStrings:Primary setting before using this legacy compose deployment.
- Review the existing RhinoCompute launcher path, application URLs, CORS, BAT task names and health URLs before starting this snapshot on a new instance.
- No database migration, application startup, email delivery or deployment was run as part of this upload.

Secrets must be supplied outside Git. appsettings.Local.json is ignored but is not automatically loaded by this snapshot; explicitly configure loading if you choose to use it. ASP.NET Core environment overrides or a private appsettings.Production.json are alternatives.

## Isolated Windows deployment

The Windows host tracks `main` in the three `mkt-ergono3d/test-ergono3d-*` repositories. It uses an independent Windows account, a loopback gateway at `http://127.0.0.1:18080`, and a new PostgreSQL database on loopback port `55432`. The two private repositories use individual read-only deploy keys. GitHub Actions also builds and smoke-tests the API on a GitHub-hosted runner.

- `deploy/windows-test/Initialize-TestHost.ps1` provisions a fresh test host. Run it in elevated PowerShell 7 with the approved EC2 instance ID and a verified portable .NET 8 SDK directory. The host uses the installed Node.js, Git, OpenSSH, and PostgreSQL 17 binaries.
- `Ergono3D Deployment Test - Sync` checks for updates at boot and every minute. It builds each commit in an independent directory and submits the release for activation. `Ergono3D Deployment Test - Host` starts at boot and monitors the API and Heel processes. The gateway stays on the active release until the candidate passes its own health check.
- The deployment root is `C:\ProgramData\Ergono3D\DeploymentTest`. Logs are under `logs`, release files under `releases`, and the active and previous commit IDs are recorded in `state\active.json`. Keep at least the active and previous release directories for each repository.
- API process health is `/health`. `/health/ready` also checks the isolated database and reports the active API commit. Rhino uses `RhinoCompute__ApiKey` from private runtime configuration. Keep production integrations out of `private\runtime.json`.
- Configure `rhino` in private `runtime.json` only after the instance has an authorized test license. The test launcher uses loopback port `16500` and starts Compute on the first STL request. Create a dedicated empty `Temp` directory next to the launcher. Grant the test account write access to `Temp` and `rhino-runtime.log`. Set `RhinoCompute__BaseUrl`, `RhinoCompute__LauncherPath`, `RhinoCompute__ApiKey`, `RhinoCompute__IdleSeconds` and the authorized `RHINO_TOKEN` in that section. Older releases can still run during a rollback with Compute disabled.
- The Windows test account needs read and execute access to the installed StarDesigner plugin package, including its `FootModel` and `Pads` directories. Keep that package read-only for the test account.
- To restore a previous release, place JSON containing its `commit` in `pending\api.json`, `pending\landing.json`, or `pending\app.json`, as appropriate. Use an atomic rename from a temporary file in the same directory. The host applies the same readiness checks to the rollback candidate and records the result in `state\active.json`.
