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
