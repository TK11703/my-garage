# Implementation Plan: GitHub CI/CD Pipeline

**Branch**: `008-github-cicd-pipeline` | **Date**: 2026-11-03 (revised 2026-11-03) | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/008-github-cicd-pipeline/spec.md`, plus planning input: keep cost minimal; run the web and API as scale-to-zero containers in the existing shared Container Apps environment (resource group `rg-apps`, `northcentralus`) and shared container registry; create a new database on the existing shared PostgreSQL server; put a new storage account in `rg-platform`; describe everything deployed with Bicep; create the email-intake Logic App with a tracked PowerShell script that stays outside the pipeline, reads the mailbox `mygarage-intake@outlook.com` through a connection the owner provides, and calls the API with a managed identity.

**Revision (owner corrections)**: applications in `rg-apps`; storage in `rg-platform`; three identities (`id-mygarage-api`, `id-mygarage-web`, `id-mygarage-intake`); new Entra registrations for Azure; `mygarage-github-oidc` with subscription Contributor and User Access Administrator for now; add `/health` and `/alive`; no Defender; Entra token database access in Azure (password stays acceptable locally); web sign-in with a federated credential on `id-mygarage-web` instead of a client secret; Linux test adjustments expected.

## Summary

Add two GitHub Actions workflows and a small set of tracked infrastructure assets:

- **CI** (`ci.yml`, reusable `verify.yml`): on every pull request and every push to `main`, build the solution with warnings as errors, run all four test projects on an Ubuntu runner (Docker available for Testcontainers and the AppHost smoke tests), compile and lint the Bicep, and report one always-present required check named `verify`. Documentation-only changes skip the heavy jobs but still satisfy the check.
- **CD** (`deploy.yml`): on push to `main` (and by manual dispatch for a chosen commit), re-use `verify`, publish the API and web container images once per commit to the shared registry with the .NET SDK container support (no Dockerfile), then deploy `infra/bicep` into `rg-apps` inside GitHub environment `production`, wait for both apps to report healthy, and summarise the run. GitHub signs in to Azure with OpenID Connect federation (no stored Azure secret).
- **Azure shape**: two Container Apps (`ca-api-mygarage`, `ca-web-mygarage`, 0.25 vCPU / 0.5 GiB, minimum replicas 0) in `cae-shared`, pulling images from `acccrshared` with the existing shared pull identity; a new database `mygarage` on `accpsqlshared`; one new storage account in `rg-platform`; user-assigned identities `id-mygarage-api` and `id-mygarage-web` (Bicep) and `id-mygarage-intake` (Logic App script).
- **One-time preparation** (owner-run, repeatable scripts, not part of the pipeline): `infra/azure/Initialize-Azure.ps1` (GitHub environment, pipeline identity with federated credentials and subscription rights, the identities and database, the database's Entra principal) calling an extended `infra/entra/Initialize-Entra.ps1` (new Azure-only registrations), and `infra/github/Initialize-GitHub.ps1` (branch protection ruleset, optional environment reviewer).
- **Logic App** (outside the pipeline): `infra/logic-app/New-IntakeLogicApp.ps1` creates `id-mygarage-intake` and a Consumption Logic App using it, wires it to the Outlook connection `con-mygarage-outlook` that the script creates (the owner authorises it once in the portal), grants the identity the `Email.Ingest` app role, and posts each email to the deployed API.
- **Application changes** (three, all small): (1) expose the existing anonymous `/health` and `/alive` endpoints in every environment, not just Development (implemented now, test-first); (2) let the API authenticate to PostgreSQL with a Microsoft Entra token when `Database__UseEntraAuthentication` is true, leaving local development on a password; (3) let the web app authenticate to Entra with a managed-identity federated credential when `AzureAd__Web__ManagedIdentityClientId` is set, leaving local development on a client secret.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (unchanged; SDK `10.0.403` pinned by `global.json`). New artifacts: GitHub Actions YAML, Bicep, PowerShell 7.

**Primary Dependencies**: no new NuGet packages beyond one explicit pin: `Azure.Identity` 1.21.0 (already a transitive dependency of `Aspire.Azure.Storage.Blobs`) is now referenced by `MyGarage.Api` because on a clean restore the transitive 1.17.2 duplicated types that `Azure.Core` 1.62 also contains, which broke the Release build (CS0433) that CI runs; 1.21.0 removes the duplication. Npgsql is already referenced. GitHub Actions `actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact`, `azure/login` (all pinned by commit SHA, kept current by Dependabot). Azure CLI and Bicep CLI (preinstalled on `ubuntu-latest`), .NET SDK container publishing (`-t:PublishContainer`), GitHub CLI for the one-time scripts.

**Storage**: existing shared PostgreSQL flexible server `accpsqlshared` (new database `mygarage`, owned by the Entra principal `id-mygarage-api`); one new Standard LRS StorageV2 account in `rg-platform` for the private blob containers `vehicle-images`, `email-intake`, `vehicle-documents`.

**Testing**: xUnit (existing suites, Testcontainers PostgreSQL, Aspire testing). All three application changes are test-first. Infrastructure is verified by Bicep compile and lint in CI, `what-if` and post-deploy health probes in CD, `-WhatIf` and idempotent re-runs for scripts, and the [quickstart](quickstart.md). Tests that fail only because they were written on Windows are corrected to be OS-neutral.

**Target Platform**: Azure Container Apps (Consumption profile) in `northcentralus`, environment `cae-shared` (`rg-apps`); GitHub-hosted `ubuntu-latest` runners.

**Project Type**: existing web application (Blazor web front end + minimal API) plus delivery infrastructure; no new .NET project.

**Performance Goals**: pull-request verification result within 15 minutes of the last push; merge to healthy deployment within 20 minutes (SC-002, SC-003). A cold start from zero (web calling API) should serve a first page in well under a minute.

**Constraints**: minimum cost (scale to zero, shared platform resources, Basic registry, Consumption Logic App polling every 5 minutes, no Defender); the shared PostgreSQL server allows 50 connections in total, so the API keeps a pool of at most 10; shared resources are modified only by adding this app's database and role/access grants; no secrets at all stored in GitHub or in Azure (no Azure secret, no database password, no web client secret); the web app keeps its in-process session design, so it runs a single replica.

**Scale/Scope**: single `production` environment; a handful of users; API at most 2 replicas, web exactly 0 or 1.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / rule | Assessment |
|---|---|
| I. .NET 10 and C# only | Pass. Application projects stay C# on .NET 10 with warnings as errors; the new YAML, Bicep, and PowerShell are delivery assets, not application projects. |
| II. Feature-folder organization | Pass. The two code changes sit in `MyGarage.ServiceDefaults` (cross-cutting, allowed) and `MyGarage.Api/Common/` (cross-feature code). Delivery assets are grouped by concern under `infra/` and `.github/`. |
| III. Solution structure | Pass. No project is added or re-referenced. `MyGarage.Database` is untouched (token handling lives in the API host that already owns the `NpgsqlDataSource` registration). |
| IV. DTO boundary, V. Validated minimal APIs | Pass. No endpoint, DTO, or contract changes. The health endpoints are framework health checks, see Complexity Tracking. |
| VI. Dapper/PostgreSQL/SQL files, VII. Versioned migrations | Pass. Unchanged. The migration runner still applies scripts at API start-up under an advisory lock, now over an Entra-authenticated connection. Rollback is code-only; schema changes must stay backward compatible for one release (documented in `infra/README.md`). |
| VIII. Test-first with real dependencies | Pass. Health-endpoint tests (Production-environment host), the token-provider unit test, and the federated-credential configuration tests are written and observed failing before the code. CI runs every suite against real PostgreSQL via Testcontainers; the database is never mocked. |
| Technology: authentication | Pass. Entra settings are supplied unchanged in shape; the web app additionally supports a managed-identity federated credential in place of a client secret (OpenID Connect and the token flow are unchanged). The Logic App uses an app-only token for the dedicated `Email.Ingest` role the API already requires. No new anonymous business endpoint. |
| Technology: observability | Pass. Console logging reaches the environment's Log Analytics workspace; no sensitive data is added to pipeline logs or summaries (FR-021). |
| Technology: dependencies | Pass. No new NuGet package, ORM, or UI framework. Third-party Actions are pinned by SHA. |
| Workflow: branch per spec, merge gates | Pass, and strengthened: branch protection will require pull requests and the `verify` check (zero-warning build, all tests, migrations from empty, Bicep valid). |

Gate result: **pass** with the justified items below. Re-check after Phase 1 design: still **pass**.

## Project Structure

### Documentation (this feature)

```text
specs/008-github-cicd-pipeline/
├── plan.md              # This file
├── research.md          # Phase 0: inventory of existing Azure resources and design decisions
├── data-model.md        # Phase 1: configuration, identity, and resource-mapping model
├── quickstart.md        # Phase 1: end-to-end validation guide
├── contracts/
│   ├── workflows.md           # Triggers, jobs, required checks, permissions
│   ├── environment-config.md  # GitHub variables/secrets and the application settings they become
│   ├── bicep-interface.md     # main.bicep parameters, resources, outputs
│   ├── azure-setup.md         # Contract of the one-time setup scripts
│   └── logic-app-intake.md    # Logic App trigger, mapping to the intake request, failure handling
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks, not created here)
```

### Source Code (repository root)

```text
.github/
├── workflows/
│   ├── verify.yml            # Reusable (workflow_call): build, test, migrations, Bicep checks; job `verify`
│   ├── ci.yml                # pull_request -> verify
│   └── deploy.yml            # push to main / workflow_dispatch -> verify -> build images -> deploy -> health
├── scripts/
│   ├── Write-TestSummary.ps1 # Turns TRX files into a step summary of failures
│   └── Test-Health.ps1       # Polls /health of both apps and checks the live revision runs the new tag
└── dependabot.yml            # github-actions + nuget (grouped) update PRs

infra/
├── README.md                 # Extended: pipeline, resource mapping, runbook, rollback, rotation
├── bicep/
│   ├── main.bicep            # Resource-group scope (rg-apps): wires modules together
│   ├── main.bicepparam       # Reads values from environment variables (no secrets in the file)
│   ├── bicepconfig.json      # Lint rules at error level
│   └── modules/
│       ├── identities.bicep        # id-mygarage-api and id-mygarage-web (rg-apps)
│       ├── storage.bicep           # Storage account in rg-platform + blob role for the API identity
│       ├── platform-database.bicep # Database `mygarage` on the existing server (rg-platform scope)
│       ├── ai-access.bicep         # OpenAI User role for the API identity on the existing AI account
│       └── container-apps.bicep    # API and web Container Apps in the existing environment
├── azure/
│   ├── README.md
│   └── Initialize-Azure.ps1  # Repeatable one-time preparation (GitHub env, OIDC identity, identities, DB principal)
├── github/
│   ├── README.md
│   └── Initialize-GitHub.ps1 # Branch protection ruleset, optional environment reviewer (via gh)
├── logic-app/
│   ├── README.md
│   ├── New-IntakeLogicApp.ps1     # Not part of the pipeline; creates/updates identity, Logic App, role grant
│   └── workflow.definition.json   # Workflow definition with parameters, tracked in git
└── entra/
    └── Initialize-Entra.ps1  # Extended with -WebBaseUri, -GitHubEnvironment, -ManagedIdentityPrincipalId (defaults unchanged)

src/
├── MyGarage.ServiceDefaults/
│   └── Extensions.cs         # MapDefaultEndpoints: map /health and /alive in all environments
├── MyGarage.Web/
│   ├── Common/EntraWebSettings.cs                  # ManagedIdentityClientId; secret required only without it
│   └── Authentication/AuthenticationExtensions.cs  # SignedAssertionFromManagedIdentity credential when configured
└── MyGarage.Api/
    ├── Program.cs            # AddNpgsqlDataSource gains the Entra-token hook
    └── Common/
        └── EntraDatabaseAuthentication.cs   # Token-backed password provider, enabled by Database:UseEntraAuthentication

tests/
├── MyGarage.Api.Tests/
│   └── Common/               # health-endpoint test (Production host) + Entra token provider unit test
└── MyGarage.Web.Tests/
    └── Common/               # health-endpoint test (Production host) + federated-credential configuration tests
```

**Structure Decision**: keep the existing multi-project layout untouched. Delivery assets live in `.github/` (workflows, helper scripts) and `infra/` (Bicep, one-time scripts, the Logic App), consistent with the existing `infra/entra` convention. Only `MyGarage.ServiceDefaults`, `MyGarage.Api/Common`, `MyGarage.Web` authentication files, and the matching test folders change in `src/` and `tests/`.

## Complexity Tracking

| Violation / deviation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Anonymous `/health` and `/alive` in Production (constitution: every endpoint requires authorization unless the spec justifies) | Container Apps probes and the pipeline's post-deploy check must reach them without a token; they return only the status word and already carry `AllowAnonymous` in Development. Justified here: no user, vehicle, or record data is exposed. | TCP-only probes cannot tell the pipeline whether the new version is actually healthy (FR-010), and an authenticated probe is not possible for the platform. |
| Pipeline identity holds Contributor and User Access Administrator at subscription scope (spec FR-019 asks for resource-scoped rights) | Owner decision, to rule out permission problems while the pipeline is brought up. Temporary: it still stores no Azure secret and trusts only this repository's `production` environment and `main` branch. | Resource-scoped roles and custom roles are the intended end state; they are deferred to a follow-up (research R5 lists the target scopes). |
