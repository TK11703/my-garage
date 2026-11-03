# Tests

Use this folder for automated test projects and supporting test assets, including unit, integration, and end-to-end tests.

Mirror the structure of the projects under test, and keep tests deterministic, isolated, and repeatable.

The Database, Api, and AppHost tests start real PostgreSQL and Azurite containers (Testcontainers / Aspire testing), so **Docker must be running** to run them. Each test class works in its own empty database; the database is never mocked. The Web tests need no Docker. Do not run tests while `aspire run` is active. The GitHub `verify` check runs every suite on Linux (`ubuntu-latest`, Docker available), so tests must not depend on Windows paths, line endings, or file-name case.

## Email ingestion (feature 007)

- Only the external AI service is replaced: Api tests register `FakeDraftExtractor` (a queue of canned results, a call log, optional failures) as `IDraftExtractor`, and extractor client tests use a fake `HttpMessageHandler`. Blob storage is real Azurite; each `ApiFactory` uses its own `email-intake` and `vehicle-documents` container names so parallel test classes never share one.
- `ApiFactory` signs tests in as a user (`CreateClientForUser`, scope `access_as_user`), as the workflow (`CreateClientForWorkflow`, app role `Email.Ingest`, no scope), or with neither role nor scope. `IntakeClient` builds intake requests, and `EmailIntakeTestData` seeds profiles, submissions, attachments, drafts, notifications, and documents directly with SQL.
- Hosted services are switched off in `ApiFactory` (`EmailIntake:Processing:Enabled` and `Drafts:Expiry:Enabled` are `false`); tests call `SubmissionProcessor.ProcessOnceAsync` and `DraftExpiryService.RunOnceAsync` themselves.
- `EmailIntake/AuthorizationMatrixTests` reads every route from `specs/007-email-ingestion-api/contracts/api.openapi.yaml` and checks anonymous, workflow-token, user-token-on-intake, and cross-user access. `EmailIntake/LogContentTests` proves no email content reaches logs, metric tags, or failure reasons.
- Tests that capture logs share the static Serilog logger and must stay in `[Collection("Logging")]` so two hosts never run at once.
