---

description: "Task list for GitHub CI/CD Pipeline"
---

# Tasks: GitHub CI/CD Pipeline

**Input**: Design documents from `/specs/008-github-cicd-pipeline/`

**Prerequisites**: plan.md, spec.md, research.md (R1-R17), data-model.md, contracts/ (`workflows.md`, `environment-config.md`, `bicep-interface.md`, `azure-setup.md`, `logic-app-intake.md`), quickstart.md

**Tests**: Included where application code changes (constitution VIII: write the test, watch it fail, then implement). Infrastructure assets are validated instead of unit tested: Bicep compile and lint, `what-if`, `-WhatIf` and re-run idempotency for scripts, the failure rehearsals, and [quickstart.md](quickstart.md). The database is never mocked.

**Organization**: Tasks are grouped by user story. Story numbers match spec.md: US1 verify every change (P1), US2 deploy `main` automatically (P1), US3 discover and reuse the existing Azure resources (P2), US4 keep configuration and secrets out of the repository (P2), US5 control and observe releases (P3). Phase order follows dependencies, not only priority: the Foundational phase (three small application changes the deployed app needs) and US3 (the one-time Azure preparation) must finish before US2 can deploy; US1 stands alone and ships first.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1-US5, only on user-story phase tasks
- Names (keep identical everywhere): resource group `rg-apps` (apps, identities, Logic App) and `rg-platform` (storage, database, registry, AI account), region `northcentralus`; environment `cae-shared`; registry `acccrshared.azurecr.io` with repositories `mygarage/api` and `mygarage/web`; pull identity `id-shared-acrpull`; database server `accpsqlshared`, database `mygarage`; AI account `aif-shared-acc` deployment `gpt-4.1-mini`; apps `ca-api-mygarage` and `ca-web-mygarage`; identities `id-mygarage-api`, `id-mygarage-web`, `id-mygarage-intake`; Logic App `logic-mygarage-intake`; pipeline registration `mygarage-github-oidc` with federated credentials `github-environment-production` and `github-main-branch`; Entra registrations `MyGarage Azure API` and `MyGarage Azure Web` (federated credential `mygarage-web-managed-identity`); GitHub environment `production`; required check `verify`; image tag `sha-` followed by the full 40-character commit SHA (44 characters in total)
- No new NuGet packages (Npgsql, Azure.Core, and Microsoft.Identity.Web 4.16.1 already cover everything). Third-party GitHub Actions are pinned to full commit SHAs with the version in a trailing comment
- Workflow `permissions` default to `contents: read`; `id-token: write` only on jobs that sign in to Azure; no secret exists in the GitHub environment (environment variables only)
- Scripts are PowerShell 7, support `-WhatIf`, are idempotent (report `created`, `updated`, or `unchanged`), never print a secret, and never delete or replace an existing resource
- Never run `dotnet test` while `aspire run` is active (file locks, long stalls)
- When a step needs the owner signed in (Azure, GitHub, Entra, or the web app), ask the owner to sign in and then continue

## Phase 1: Setup

**Purpose**: Baseline and tooling for everything that follows.

- [X] T001 On branch `008-github-cicd-pipeline`, run `dotnet build MyGarage.slnx` and confirm zero warnings; run `dotnet test tests/MyGarage.Web.Tests` (no Docker needed) to record the baseline; note that `src/MyGarage.ServiceDefaults/Extensions.cs` and the two `HealthEndpointsTests.cs` files already carry the health-endpoint change (T004, T005)
- [X] T002 [P] Confirm tooling for the one-time scripts and record versions in the PR description only (no file): `az version`, `az bicep version` (install with `az bicep install` if missing), the `rdbms-connect` extension (`az extension add --name rdbms-connect`), `gh auth status` as repository administrator, PowerShell 7
- [X] T003 [P] Create `infra/bicep/bicepconfig.json` enabling the Bicep linter with every rule at level `error` (`use-recent-api-versions`, `no-unused-params`, `no-unused-vars`, `no-hardcoded-env-urls`, `secure-secrets-in-params`, `outputs-should-not-contain-secrets`, `use-secure-value-for-secure-inputs`, `no-unnecessary-metadata`) and `"analyzers": { "core": { "enabled": true } }`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The three small application changes the deployed environment needs. They are test-first (constitution VIII); each test is run and seen to fail before the implementation. Local development behaviour must not change.

**CRITICAL**: US2 cannot deploy a healthy app until this phase is complete.

### Health endpoints (research R10)

- [X] T004 [P] `HealthEndpointsTests` in `tests/MyGarage.Api.Tests/Common/HealthEndpointsTests.cs` and `tests/MyGarage.Web.Tests/Common/HealthEndpointsTests.cs`: a host built with environment `Production` answers `GET /alive` and `GET /health` with 200, no token, and a body that is exactly `Healthy`; confirmed failing before T005
- [X] T005 Change `MapDefaultEndpoints` in `src/MyGarage.ServiceDefaults/Extensions.cs` to map `/health` (all checks) and `/alive` (checks tagged `live`) in every environment, still `AllowAnonymous`, default plain-status writer only; T004 passes for both projects

### Entra token database access for the API (research R8)

- [X] T006 [P] `EntraDatabaseAuthenticationTests` in `tests/MyGarage.Api.Tests/Common/EntraDatabaseAuthenticationTests.cs`, confirm they fail first: with `Database:UseEntraAuthentication` `true` the helper registers a password provider on an `NpgsqlDataSourceBuilder` and, called with a fake `TokenCredential`, requests exactly the scope `https://ossrdbms-aad.database.windows.net/.default` and returns the credential's token as the password; the provider is asked again after the refresh interval (use a fake `TimeProvider`-free design: test the static `GetPasswordAsync(TokenCredential, CancellationToken)` method directly); with the setting missing or `false` the builder is left without a password provider (connection string password keeps working, local development unchanged); a missing token (credential throws) surfaces the exception rather than returning an empty password
- [X] T007 Create `src/MyGarage.Api/Common/EntraDatabaseAuthentication.cs`: static `Configure(NpgsqlDataSourceBuilder builder, IConfiguration configuration, TokenCredential credential)` that, only when `Database:UseEntraAuthentication` is `true`, calls `builder.UsePeriodicPasswordProvider(...)` with a callback that returns `GetPasswordAsync(credential, ct)`, success refresh interval 55 minutes and failure refresh interval 5 seconds; `GetPasswordAsync` gets a token for scope `https://ossrdbms-aad.database.windows.net/.default` and returns `token.Token`; nullable-safe, async, forwards the `CancellationToken`. Change `src/MyGarage.Api/Program.cs`: `builder.AddNpgsqlDataSource("garagedb", configureDataSourceBuilder: dataSource => EntraDatabaseAuthentication.Configure(dataSource, builder.Configuration, new DefaultAzureCredential()))` (`DefaultAzureCredential` honours `AZURE_CLIENT_ID`, so it picks `id-mygarage-api`). No new package; T006 passes

### Federated credential for the web app (research R17)

- [X] T008 [P] Federated-credential tests in `tests/MyGarage.Web.Tests/Common/EntraWebSettingsTests.cs` and `tests/MyGarage.Web.Tests/Authentication/FederatedCredentialTests.cs`, confirm they fail first: (a) `EntraWebSettings.Load` succeeds when `AzureAd:Web:ManagedIdentityClientId` is set and `AzureAd:Web:ClientSecret` is empty, still lists `AzureAd:Web:ClientSecret` as missing when neither is set, and still succeeds with only the secret; (b) a `WebAppFactory` host built with `WithWebHostBuilder` and in-memory configuration `AzureAd:Web:ManagedIdentityClientId` set and `AzureAd:Web:ClientSecret` empty resolves `IOptionsMonitor<MicrosoftIdentityOptions>` for the OpenID Connect scheme with exactly one entry in `ClientCredentials` whose `SourceType` is `ClientCredentialSourceType.SignedAssertionFromManagedIdentity` and `ManagedIdentityClientId` equal to the configured value, and no client secret; (c) a host with a secret and no managed identity client id keeps a secret credential and no managed identity credential
- [X] T009 In `src/MyGarage.Web/Common/EntraWebSettings.cs` add `ManagedIdentityClientId` (`string`, default empty) to `WebSection` and require `AzureAd:Web:ClientSecret` only when it is empty; in `src/MyGarage.Web/Authentication/AuthenticationExtensions.cs` configure `options.ClientCredentials = [ new CredentialDescription { SourceType = CredentialSource.SignedAssertionFromManagedIdentity, ManagedIdentityClientId = ... } ]` when `ManagedIdentityClientId` is set, else keep `options.ClientSecret = settings.Web.ClientSecret`; sign-in and `EnableTokenAcquisitionToCallDownstreamApi` use the same options; nullable-safe; no new package; T008 passes
- [X] T010 Run `dotnet build MyGarage.slnx` (zero warnings), `dotnet test tests/MyGarage.Web.Tests`, and `dotnet test tests/MyGarage.Api.Tests` (Docker needed); every pre-existing test still passes. Commit with a Conventional Commits message such as `feat: add production health endpoints, Entra database auth, and web federated credential`

**Checkpoint**: the application can run in Azure with probes, a passwordless database connection, and no client secret; local development is unchanged.

---

## Phase 3: User Story 1 - Verify every change before it can merge (Priority: P1) MVP

**Goal**: Every pull request to `main` gets a build, the full test suite, a migrations-from-empty check, and a Bicep check, reported as the required check `verify`; a failing check blocks the merge.

**Independent Test**: Quickstart steps 2 and 3. A healthy pull request passes; a compiler warning, a failing test, and an edited applied migration each fail `verify`; a documentation-only pull request passes with the heavy jobs skipped; a superseded run is cancelled; the merge button is blocked while `verify` is not green.

### Implementation for User Story 1

- [X] T011 [P] [US1] Create `.github/scripts/Write-TestSummary.ps1` (PowerShell 7): parameter `-ResultsPath`; read every `*.trx` under it, count passed/failed/skipped, and append to `$env:GITHUB_STEP_SUMMARY` a table of totals plus, for each failed test, its name and the first 15 lines of its message and stack trace (no output captured from passing tests); exit 0 always; no test output beyond failures is written (FR-021)
- [X] T012 [US1] Create `.github/workflows/verify.yml` per contracts/workflows.md: `on: workflow_call` with string input `ref` (default empty = the caller's commit); top-level `permissions: contents: read`; jobs on `ubuntu-latest`, `timeout-minutes: 30`: `changes` (checkout with `fetch-depth: 0`; outputs `code` = any changed path outside `docs/`, `specs/`, `.specify/`, and `*.md`, and `bicep` = any change under `infra/bicep/`; on a push or dispatch compare with the previous commit; on a pull request compare with the base branch); `build-test` (needs `changes`, runs when `code == 'true'`; `actions/setup-dotnet` with `global-json-file: global.json`; `dotnet restore`; `dotnet build MyGarage.slnx -c Release --no-restore`; `dotnet test MyGarage.slnx -c Release --no-build --logger "trx;LogFileName=results.trx" --results-directory TestResults` so the four test projects run; always run `Write-TestSummary.ps1 -ResultsPath TestResults`; `actions/upload-artifact` for `TestResults` and the build log with 14-day retention; fail if Docker is unavailable by running `docker info` first); `bicep` (needs `changes`, runs when `bicep == 'true'` and `infra/bicep/main.bicep` exists; `az bicep build --file infra/bicep/main.bicep --stdout > /dev/null` and `az bicep lint --file infra/bicep/main.bicep`, failing on any diagnostic); there is no gate job in this file: a calling job succeeds only when every called job succeeded or was skipped (the required `verify` check lives in `ci.yml`, T013, because a reusable workflow's checks are named `<caller job> / <called job>`)
- [X] T013 [US1] Create `.github/workflows/ci.yml`: `on: pull_request` for branches `[main]` types `[opened, synchronize, reopened]`; `permissions: contents: read`; `concurrency: group: ci-${{ github.event.pull_request.number }}`, `cancel-in-progress: true`; jobs: `checks` calling `./.github/workflows/verify.yml`, and job `verify` (`needs: checks`, `if: always()`, name exactly `verify`; succeeds only when the `checks` result is `success`, so it also reports on documentation-only pull requests); no secrets, no `id-token` (fork pull requests run safely, FR-022)
- [X] T014 [P] [US1] Create `.github/dependabot.yml`: version 2, `github-actions` ecosystem at `/` weekly, and `nuget` at `/` weekly with grouped minor/patch updates; commit-message prefix `build` for NuGet and `ci` for Actions (FR-027)
- [X] T015 [US1] Resolve full commit SHAs for every action used in `verify.yml` and `ci.yml` (`actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact`) with `gh api repos/<owner>/<repo>/git/ref/tags/<tag> --jq .object.sha` (follow annotated tags to the commit) and pin each `uses:` as `owner/repo@<40-hex-sha> # vX.Y.Z`; no floating tag remains
- [X] T016 [US1] Confirm the migrations-from-empty check (FR-003) is covered: `tests/MyGarage.Database.Tests/Common/MigrationRunnerTests.cs` applies all scripts to an empty Testcontainers database and fails on applied-script checksum drift; if either assertion is missing, add the test (test-first) so `verify` enforces it. Finding: those tests cover the empty-database apply and a tampered ledger, but nothing fails when a script file in git is edited, so `verify.yml` gained a pull-request job `migrations` that fails when an existing migration script is modified, renamed, or deleted
- [X] T017 [US1] Push the branch, open a **draft** pull request to `main`, let `ci.yml` run on `ubuntu-latest`, and collect every failure (expected: tests written on Windows). Record the list of failing tests and causes in the PR description
- [X] T018 [US1] Fix every Linux-only failure by making the test OS-neutral, never by skipping, deleting, or changing application behaviour: likely causes are path separators and `Path.Combine` assumptions (for example the `.razor` source scan in `tests/MyGarage.Web.Tests/UiStandardConformanceTests`), file-name case sensitivity, line endings (`\r\n` vs `\n`), time zone or culture, and port or timing assumptions. Re-run until `verify` is green on the pull request; commit each fix with `test:` or `fix:` messages
- [x] T019 [US1] Failure rehearsals from quickstart step 2 on throwaway branches (never merged): add a compiler warning and confirm `verify` fails naming it; break one test and confirm the summary lists it; edit an applied migration script and confirm the `migrations` job fails; change only a Markdown file and confirm `verify` passes with `build-test` skipped; push twice quickly and confirm the first run is cancelled
- [X] T020 [P] [US1] Create `infra/github/Initialize-GitHub.ps1` and `infra/github/README.md` per contracts/azure-setup.md: parameters `-Repository` (default `TK11703/my-garage`), `-RequiredReviewer`, `-WhatIf`; with `gh api`: create or update a repository ruleset on `main` requiring a pull request and the status check `verify`, blocking force pushes and deletion; set the default workflow token to read-only; when `-RequiredReviewer` is given add it as a required reviewer on environment `production` (creating the environment when missing) and keep `prevent_self_review` false (a single owner must be able to approve their own deployment); add switch `-RestrictEnvironmentToMain` that limits deployments to the `production` environment to branch `main` (off by default so the throwaway rehearsals in T038 and T050 can run; T060 turns it on); compare current state first and report `created`, `updated`, or `unchanged`; `-WhatIf` changes nothing. Current state (owner, 2026-11-03): environment `production` already exists with `TK11703` as required reviewer, self-review allowed, and no branch restriction, so the first run reports those as `unchanged`
- [X] T021 [US1] After `verify` has reported once (T017), run `./infra/github/Initialize-GitHub.ps1 -WhatIf` then the real run, run it again (all `unchanged`), and confirm a pull request with a failing or running `verify` cannot be merged and a direct push to `main` is refused (quickstart step 3). Mark the pull request ready and merge it only when the owner decides

**Checkpoint**: User Story 1 is complete: `main` is protected and every change is verified on Linux.

---

## Phase 4: User Story 3 - Discover and reuse the existing Azure resources safely (Priority: P2)

**Goal**: The existing shared resources are mapped to the application, the one-time preparation exists as repeatable scripts, nothing shared is deleted or replaced, and a second run changes nothing. This phase is a prerequisite of US2.

**Independent Test**: Quickstart step 1: `-WhatIf` lists the intended changes; the real run reports `created` once; a second run reports everything `unchanged`; only the documented additions appear on shared resources.

### Implementation for User Story 3

- [X] T022 [P] [US3] Extend `infra/entra/Initialize-Entra.ps1` and `infra/entra/README.md` (defaults unchanged, existing behaviour for `MyGarage API`/`MyGarage Web` untouched): add parameters `-WebBaseUri` (when given, redirect URIs are `<uri>/signin-oidc` and `<uri>/signout-callback-oidc` instead of the localhost ones), `-GitHubEnvironment` (when given, write `AZURE_TENANT_ID`, `API_ENTRA_CLIENT_ID`, `API_ENTRA_SCOPE`, and `WEB_ENTRA_CLIENT_ID` as variables of that environment with `gh variable set --env` and do not read or write the AppHost user-secrets), `-ManagedIdentityPrincipalId` (when given, create federated identity credential `mygarage-web-managed-identity` on the web registration through Microsoft Graph with issuer `https://login.microsoftonline.com/<tenant id>/v2.0`, subject the principal id, audience `api://AzureADTokenExchange`, skipping when it already exists, and create no client secret), and `-Repository` (default `TK11703/my-garage`). `-WhatIf` lists the federated credential and variables it would set; nothing secret is printed
- [X] T023 [P] [US3] Create `infra/bicep/modules/identities.bicep` (scope: resource group): user-assigned identities `id-mygarage-api` and `id-mygarage-web` in the given location; outputs for each: `resourceId`, `clientId`, `principalId`; no other resource
- [X] T024 [P] [US3] Create `infra/bicep/modules/platform-database.bicep` (scope: `rg-platform`): parameters `serverName` and `databaseName` (default `mygarage`); the server is an `existing` `Microsoft.DBforPostgreSQL/flexibleServers` resource and the module declares only the child database with `charset` `UTF8` and `collation` `en_US.utf8`; no server-level setting, role, or firewall rule is declared
- [X] T025 [US3] Create `infra/azure/Initialize-Azure.ps1` per contracts/azure-setup.md (parameters `-SubscriptionId`, `-ResourceGroup` default `rg-apps`, `-PlatformResourceGroup` default `rg-platform`, `-Location` default `northcentralus`, `-Repository`, `-WhatIf`): (1) create GitHub environment `production` when missing (it already exists with its reviewer, so report `unchanged` and never alter its protection rules); (2) create app registration and service principal `mygarage-github-oidc` with federated credentials `github-environment-production` and `github-main-branch`, issuer `https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange`, subjects `<prefix>:environment:production` and `<prefix>:ref:refs/heads/main` where `<prefix>` is `sub_claim_prefix` from `gh api repos/<repo>/actions/oidc/customization/sub` (never hard-coded); (3) assign it Contributor and User Access Administrator at subscription scope (temporary, owner decision, spec FR-019 exception) and AcrPush on `acccrshared`; (4) deploy `infra/bicep/modules/identities.bicep` to `rg-apps`; (5) run `infra/entra/Initialize-Entra.ps1 -NamePrefix 'MyGarage Azure' -WebBaseUri https://ca-web-mygarage.<default domain of cae-shared> -ManagedIdentityPrincipalId <principal id of id-mygarage-web> -GitHubEnvironment production -Repository <repo>` (default domain read from `cae-shared`); (6) deploy `infra/bicep/modules/platform-database.bicep` to `rg-platform`; (7) as the signed-in Entra admin (access token from `az account get-access-token --resource-type oss-rdbms`, run with `az postgres flexible-server execute` against database `postgres` then `mygarage`): `SELECT * FROM pgaadauth_create_principal('id-mygarage-api', false, false)` when the principal is missing, `ALTER DATABASE mygarage OWNER TO "id-mygarage-api"`, `REVOKE CONNECT ON DATABASE mygarage FROM PUBLIC`; (8) set environment variables `AZURE_CLIENT_ID` (the pipeline registration), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `EXTRACTION_ENDPOINT` = `https://aif-shared-acc.openai.azure.com`, `EXTRACTION_DEPLOYMENT` = `gpt-4.1-mini` with `gh variable set --env production`. Each step compares current state first and reports `created`, `updated`, or `unchanged`; `-WhatIf` changes nothing; no secret is created or printed; nothing is deleted or replaced; stop with a clear message when the signed-in account lacks a needed right or the shared firewall blocks the caller
- [X] T026 [P] [US3] Create `infra/azure/README.md`: purpose, prerequisites (from contracts/azure-setup.md), parameters, the eight steps and their idempotency, the temporary subscription-wide rights and the target narrower scopes from research R5 as a follow-up list, how to recover from drift (re-run), and how to remove the app's resources manually (nothing is automated)
- [X] T027 [US3] Update `infra/README.md` with the resource mapping (FR-015): a table of each component, its existing resource, group, and whether reused, new, or gap (from research.md inventory and data-model.md), and the line that the apps and identities live in `rg-apps` while storage, database, registry, and the AI account live in `rg-platform`; update the "Azure Storage for vehicle pictures" and "Email ingestion" paragraphs so they point to the Bicep that now creates the account, containers (created by the API at start-up), role assignments, and settings; remove the note that no templates exist yet
- [X] T028 [US3] Run `./infra/azure/Initialize-Azure.ps1 -WhatIf`, then the real run with the owner signed in (`az login`, `gh auth login`), then a second run and confirm every step reports `unchanged` (SC-007). Verify: `az role assignment list --scope <acccrshared id>` shows exactly one new AcrPush entry; `az postgres flexible-server db list` shows exactly one new database `mygarage`; `az identity list -g rg-apps` shows `id-mygarage-api` and `id-mygarage-web`; `az ad app credential list` for `MyGarage Azure Web` returns no secret and its federated credential exists; the GitHub environment `production` holds the variables of contracts/environment-config.md and no secret; nothing shared was deleted or replaced (SC-008)

**Checkpoint**: The Azure side is prepared; the pipeline identity, identities, database principal, and registrations exist.

---

## Phase 5: User Story 2 - Deploy `main` automatically to the existing Azure environment (Priority: P1)

**Goal**: Merging to `main` builds the images once, deploys the Bicep to `rg-apps` with the verified commit, applies migrations through the API's start-up, waits for both apps to report healthy, and leaves the previous revision serving if the new one is unhealthy.

**Independent Test**: Quickstart steps 4 and 5: merge a small change and watch the environment update with no manual step, then merge a deliberately unhealthy change and confirm the previous revision keeps serving and the run fails; two merges in quick succession deploy one at a time and end on the latest commit.

### Implementation for User Story 2

- [X] T029 [P] [US2] Create `infra/bicep/modules/storage.bicep` (scope: `rg-platform`): storage account named `stmygarage` + the first 8 characters of `uniqueString(subscription().id, 'mygarage')`, `StorageV2`, `Standard_LRS`, `minimumTlsVersion` `TLS1_2`, `supportsHttpsTrafficOnly` true, `allowSharedKeyAccess` false, `allowBlobPublicAccess` false, `defaultToOAuthAuthentication` true; role assignment *Storage Blob Data Contributor* (`ba92f5b4-2d11-453d-a403-e96b0029c9fe`) for the API identity principal id with a deterministic `guid(...)` name; outputs `accountName` and `serviceUri`; no Defender setting and no container (the API creates `vehicle-images`, `email-intake`, `vehicle-documents` at start-up)
- [X] T030 [P] [US2] Create `infra/bicep/modules/ai-access.bicep` (scope: `rg-platform`): `existing` Cognitive Services account `aif-shared-acc`; role assignment *Cognitive Services OpenAI User* (`5e0bd9bd-7b93-4f28-af87-19fc36ad61bd`) for the API identity principal id, scoped to that account, deterministic `guid(...)` name
- [X] T031 [US2] Create `infra/bicep/modules/container-apps.bicep` (scope: `rg-apps`) per contracts/bicep-interface.md and contracts/environment-config.md: `existing` environment `cae-shared` (resource group `rg-apps`) and pull identity `id-shared-acrpull` (resource group `rg-platform`); Container App `ca-api-mygarage` and `ca-web-mygarage`, `activeRevisionsMode` `Single`; images `acccrshared.azurecr.io/mygarage/api:<imageTag>` and `.../mygarage/web:<imageTag>`; registry `acccrshared.azurecr.io` authenticated with identity `id-shared-acrpull`; user-assigned identities `id-mygarage-api` + `id-shared-acrpull` (API) and `id-mygarage-web` + `id-shared-acrpull` (web); external HTTPS-only ingress, `targetPort` 8080; resources 0.25 vCPU / 0.5 Gi; scale: API `minReplicas` 0, `maxReplicas` 2, web `minReplicas` 0, `maxReplicas` 1 (exactly one replica at most, because the web app rejects sessions from another instance), HTTP scale rule 20 concurrent requests, `cooldownPeriod` 300 for the API; probes: Startup `GET /alive` period 5 s failure threshold 60, Liveness `GET /alive` period 30 s, Readiness `GET /health` period 10 s, all port 8080; plain API setting `ConnectionStrings__garagedb` = `Host=accpsqlshared.postgres.database.azure.com;Database=mygarage;Username=id-mygarage-api;SSL Mode=Require;Maximum Pool Size=10` (no password, so not a secret); API settings `AZURE_CLIENT_ID` (client id of `id-mygarage-api`), `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, `AzureAd__TenantId`, `AzureAd__Api__ClientId`, `AzureAd__Api__Scope`, `Database__UseEntraAuthentication=true`, `ConnectionStrings__blobs` (storage service URI), `EmailIntake__Extraction__Endpoint`, `EmailIntake__Extraction__Deployment`, `ReleaseId`; web settings `AZURE_CLIENT_ID` (client id of `id-mygarage-web`), `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, `AzureAd__TenantId`, `AzureAd__Web__ClientId`, `AzureAd__Web__ManagedIdentityClientId` (client id of `id-mygarage-web`), `AzureAd__Api__ClientId`, `AzureAd__Api__Scope`, `services__api__https__0` = `https://<API fqdn>`, `ReleaseId`; no client secret anywhere; the web app depends on the API app; outputs `apiAddress`, `webAddress`, `apiRevisionName`, `webRevisionName`
- [X] T032 [US2] Create `infra/bicep/main.bicep` (`targetScope = 'resourceGroup'`, deployed to `rg-apps`): parameters from contracts/environment-config.md (`imageTag` with `@minLength(44)`, `@maxLength(44)`, and a `startsWith('sha-')` guard; `tenantId`, `apiClientId`, `apiScope`, `webClientId`, `extractionEndpoint`, `extractionDeployment`, `location` default `northcentralus`, `environmentName` `cae-shared`, `environmentResourceGroup` `rg-apps`, `platformResourceGroup` `rg-platform`, `registryServer`, `pullIdentityName`, `postgresServerName`, `aiAccountName`); modules `identities` (the same definition file `Initialize-Azure.ps1` deploys once, so an incremental re-run changes nothing), `storage` and `ai-access` and `platform-database` with `scope: resourceGroup(platformResourceGroup)`, and `container-apps`; outputs per contracts/bicep-interface.md; no secure parameter, no secret in any output
- [X] T033 [US2] Create `infra/bicep/main.bicepparam`: `using 'main.bicep'`; every value from `readEnvironmentVariable('<NAME>')` (`IMAGE_TAG`, `AZURE_TENANT_ID`, `API_ENTRA_CLIENT_ID`, `API_ENTRA_SCOPE`, `WEB_ENTRA_CLIENT_ID`, `EXTRACTION_ENDPOINT`, `EXTRACTION_DEPLOYMENT`) or a literal for the existing-resource names in contracts/environment-config.md; no secret value in the file
- [X] T034 [US2] Validate locally: `az bicep build --file infra/bicep/main.bicep` and `az bicep lint --file infra/bicep/main.bicep` produce no diagnostic; then with the owner signed in, set the variables from the environment, run `az deployment group what-if -g rg-apps -f infra/bicep/main.bicep -p infra/bicep/main.bicepparam` and confirm the changes are only the two Container Apps, the storage account and its role assignment, the OpenAI role assignment, and (if absent) the database; confirm no shared resource shows as modified or deleted
- [X] T035 [P] [US2] Create `.github/scripts/Test-Health.ps1`: parameters `-ApiAddress`, `-WebAddress`, `-ExpectedTag`, `-ResourceGroup` (default `rg-apps`), `-TimeoutMinutes` (default 10); poll `GET <address>/health` for each address until HTTP 200 and body `Healthy` or the timeout; then `az containerapp show -n ca-api-mygarage` / `ca-web-mygarage` and confirm the active revision's image tag equals `-ExpectedTag` and `provisioningState` is `Succeeded` with `runningStatus` not `Failed`; print a one-line result per app (no secret, no response body beyond the status word); non-zero exit on any failure
- [X] T036 [US2] Create `.github/workflows/deploy.yml` per contracts/workflows.md: `on: push` branches `[main]` and `workflow_dispatch` with inputs `commit` (string, default empty = the selected ref) and `dryRun` (boolean, default false); `permissions: contents: read`; `concurrency: group: deploy-production`, `cancel-in-progress: false`; jobs: `verify` (calls `./.github/workflows/verify.yml` with `ref: ${{ inputs.commit }}`); `build-images` (needs `verify`; `permissions: contents: read, id-token: write`; checkout the commit; `azure/login` with `client-id: ${{ vars.AZURE_CLIENT_ID }}`, `tenant-id: ${{ vars.AZURE_TENANT_ID }}`, `subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}`; `az acr login --name acccrshared`; compute `tag=sha-<full sha>` and fail unless it is exactly 44 characters; skip the build when both `az acr manifest show -r acccrshared -n mygarage/api:<tag>` and `.../web:<tag>` exist; else `dotnet publish src/MyGarage.Api -c Release -t:PublishContainer -p:ContainerRegistry=acccrshared.azurecr.io -p:ContainerRepository=mygarage/api -p:ContainerImageTags=<tag>` and the same for `src/MyGarage.Web` with `mygarage/web`); `deploy` (needs `build-images`; `environment: production`, so the job pauses until the required reviewer approves it in GitHub (FR-013); `permissions: contents: read, id-token: write`; `azure/login`; export the environment variables for `main.bicepparam` including `IMAGE_TAG`; `az deployment group what-if -g rg-apps` always; unless `dryRun`, `az deployment group create -g rg-apps --mode Incremental -f infra/bicep/main.bicep -p infra/bicep/main.bicepparam` capturing outputs; run `.github/scripts/Test-Health.ps1` with the outputs; write the run summary). Pin every `uses:` to a full commit SHA (resolve `azure/login` as in T015). As built: a first job `resolve` validates the commit (a full SHA reachable from `main`, unless the dispatch sets `allowNonMain` for rehearsals; a push that changed only documentation is not deployable) and `verify` runs with `full: true` so a deployment is always fully verified
- [x] T037 [US2] Dispatch `deploy.yml` from the branch with `dryRun` true, approve the pending `production` deployment as the owner, and confirm it stops after what-if with no change and the summary lists the planned resources; then dispatch it for real (or merge the pull request) and confirm both Container Apps are created, `Test-Health.ps1` passes, and `az containerapp revision list` shows the new revision active at 100% traffic (quickstart step 4). Open the web address, sign in (ask the owner to sign in, then use the integrated browser), and add a vehicle (SC-009); confirm a cold start from zero serves the first page
- [ ] T038 [US2] Failure rehearsal (quickstart step 5): on a throwaway branch push a change that makes `/health` fail (for example a bad `AzureAd__Api__Scope` override in a temporary parameter), deploy it by dispatch with `allowNonMain` true, and confirm the run fails, the previous revision stays at 100% traffic, and the app remains reachable (SC-004); then dispatch the last good commit and confirm recovery (SC-006). Never merge the throwaway change
- [ ] T039 [US2] Concurrency rehearsal: dispatch two deployments for different commits in quick succession and confirm they run one at a time (the second waits) and the environment ends on the commit dispatched last (FR-011)
- [X] T040 [US2] Add to `infra/README.md` the pipeline runbook: how a merge deploys, how to read a run, the manual dispatch with `commit` and `dryRun`, how to roll back, the rule that schema changes must stay backward compatible for one release because rollback is code-only (migrations are forward-only and run when the API starts), and the known trade-off that the API's background services run only while a replica is up (research R1)

**Checkpoint**: User Stories 1, 2, and 3 work: changes are verified, merged, and deployed to the existing Azure resources.

---

## Phase 6: User Story 4 - Keep configuration and secrets out of the repository (Priority: P2)

**Goal**: No secret appears in the repository, run logs, or summaries; deployed settings come from protected environment variables; a setting change needs no repository change; sign-in works with the federated credential.

**Independent Test**: Quickstart step 6 plus step 4: scan the repository and a completed run's logs and find no secret; sign in to the deployed app and use vehicles; change a variable, redeploy, and observe the new setting without a code change.

### Implementation for User Story 4

- [X] T041 [P] [US4] Create `.github/scripts/Test-NoSecrets.ps1`: scan tracked files under `.github/`, `infra/`, `src/**/appsettings*.json`, and `*.bicepparam` for `AccountKey=`, `SharedAccessSignature=`, `sig=` query strings, `Password=` followed by a non-empty value, `client_secret` assignments with a value, and private key headers; report file and line but never the value; exit non-zero on any hit; allow the documented placeholders (`<...>`, empty, and parameter names) so Markdown and READMEs that mention names do not fail
- [X] T042 [US4] Wire `Test-NoSecrets.ps1` into `verify.yml` as a new job `secrets` (runs on every trigger, no path filter) that the `verify` gate job also needs; prove it works by adding a fake `Password=hunter2` line on a throwaway branch and watching `verify` fail without printing the value
- [X] T043 [US4] Review every workflow for FR-021 and FR-022: no `echo` of variables or tokens, no `set -x`, `permissions` minimal per contracts/workflows.md, `ci.yml` uses `pull_request` (not `pull_request_target`) and has no `secrets`/`vars` access beyond what a fork lacks anyway, `id-token: write` only on `build-images` and `deploy`; fix anything found
- [ ] T044 [US4] Verify deployed configuration: `az containerapp show -n ca-api-mygarage -g rg-apps` and the web app show every setting of contracts/environment-config.md, the API's `ConnectionStrings__garagedb` secret reference contains no password, no setting or secret holds a client secret, `az ad app credential list --id <MyGarage Azure Web client id>` is empty, and sign-in plus vehicle features work (federated credential proven, FR-023)
- [ ] T045 [US4] Prove a setting change needs no repository change: change variable `EXTRACTION_DEPLOYMENT` in the `production` environment, dispatch `deploy.yml` for the current commit, confirm the API's `EmailIntake__Extraction__Deployment` changed, then restore it and redeploy
- [ ] T046 [US4] Inspect a completed run's logs and summary (`gh run view <id> --log`): no secret value, connection string with credentials, token, email content, or user data appears (FR-021, SC-005); run `./.github/scripts/Test-NoSecrets.ps1` over the repository and confirm it passes

**Checkpoint**: User Stories 1-4 work; the repository, workflows, and deployed apps hold no stored secret.

---

## Phase 7: User Story 5 - Control and observe releases (Priority: P3)

**Goal**: The owner can redeploy any commit, see what is deployed, read a summary of each run, and is notified when a run fails.

**Independent Test**: Quickstart step 5: dispatch an earlier commit and confirm the environment returns to it with the same verification and health checks; open any run and read the summary; fail a run and receive the standard GitHub notification.

### Implementation for User Story 5

- [X] T047 [US5] In `.github/workflows/deploy.yml` finish the manual path: when `inputs.commit` is set, check out that commit for every job (full SHA required: resolve a branch or tag with `git rev-parse` and fail when the value does not resolve), verify it, reuse its images when both tags exist in the registry or build them when not, and deploy with that commit's `infra/bicep` (FR-012); reject the dispatch when the commit is not reachable from `main`
- [X] T048 [US5] Add a final step to the `deploy` job (runs `if: always()`) that writes to `$GITHUB_STEP_SUMMARY`: commit SHA, image tag, verify result, what-if resource counts by change type, the API and web addresses, the health outcome, and whether this was a dry run or a manual rollback; include no secret and no user data (FR-024)
- [X] T049 [US5] Document in `infra/README.md` how to see what is deployed (`az containerapp show -n ca-api-mygarage -g rg-apps --query "properties.template.containers[0].image"` and the `ReleaseId` setting) and how failed runs notify: GitHub sends workflow failure notifications to the person who triggered or last changed the workflow according to their notification settings; list the exact settings to enable (Settings > Notifications > Actions: "Send notifications for failed workflows only")
- [ ] T050 [US5] Rollback rehearsal (quickstart step 5.2): dispatch an earlier commit with its full SHA, confirm verify, image reuse, deployment, health check, and that the summary names that commit; then dispatch the latest commit again; also confirm that dispatching with a commit that is not on `main` is refused

**Checkpoint**: All five user stories work independently and together.

---

## Phase 8: Email intake Logic App and the deferred walkthrough (spec FR-026, SC-009)

**Purpose**: Create the Logic App (outside the pipeline) against the deployed API and complete the manual end-to-end walkthrough deferred from spec 007.

- [X] T051 [P] [US2] Create `infra/logic-app/workflow.definition.json` per contracts/logic-app-intake.md: parameters `$connections`, `ApiAddress`, `ApiClientId`, `IdentityResourceId`, `PollIntervalMinutes`; trigger Outlook.com "When a new email arrives (V3)" (`ApiConnection`, folder `Inbox`, `includeAttachments` true, recurrence from `PollIntervalMinutes`); actions: convert the HTML body to plain text (Data Operations "Html to text"; fall back to an expression that strips tags if the action is not built in, research R12), filter attachments to those with `isInline` false, at most 5, each at most 10485760 bytes decoded, compose the request body with exactly the fields of `specs/007-email-ingestion-api/contracts/intake-email.example.json` (`messageId` = `internetMessageId`, `receivedAt` = `dateTimeReceived`, `from`, `subject`, `bodyText`, `attachments[]` with `fileName`, `contentType`, `isInline` false, `contentBase64` = `contentBytes`), HTTP `POST {ApiAddress}/api/intake/emails` with `Content-Type: application/json` and authentication `ManagedServiceIdentity` with the user-assigned `IdentityResourceId` and audience `api://{ApiClientId}`, retry policy exponential with 4 attempts honouring `Retry-After` only for 429 and 503, secure inputs and outputs on the HTTP action, on 413 resend once without attachments, on 400/401/403 stop without retry and leave the run failed (contract table)
- [X] T052 [US2] Create `infra/logic-app/New-IntakeLogicApp.ps1` per contracts/logic-app-intake.md: parameters `-OutlookConnectionId` (required), `-ResourceGroup` (`rg-apps`), `-Location` (`northcentralus`), `-Name` (`logic-mygarage-intake`), `-IdentityName` (`id-mygarage-intake`), `-ApiAddress` (default read from `ca-api-mygarage`), `-ApiClientId` (default read from the `MyGarage Azure API` registration), `-PollIntervalMinutes` (5), `-WhatIf`; steps: check the connection exists, is in the same region, and reports `Connected` (stop with instructions otherwise; never create or consent to the connection); create identity `id-mygarage-intake` when missing; create or update the Consumption Logic App from `workflow.definition.json` with that identity assigned; assign the identity's service principal the `Email.Ingest` app role on `MyGarage Azure API` through Microsoft Graph (`az rest`, `POST /servicePrincipals/{apiSpId}/appRoleAssignedTo`), skipping when already assigned; print name, state, and interval; each step reports `created`, `updated`, or `unchanged`; no secret exists or is printed
- [X] T053 [P] [US2] Create `infra/logic-app/README.md` (what it is, that it is not part of the pipeline, prerequisites including that the owner creates and authorises the Outlook connection for `mygarage-intake@outlook.com` in `northcentralus`, parameters, the mapping and retry behaviour, cost note for 5-minute polling) and update the "Logic App contract" section of `infra/README.md` with the deployed address pattern and the `id-mygarage-intake` identity
- [ ] T054 [US2] With the owner (who creates the Outlook connection and signs in), run `./infra/logic-app/New-IntakeLogicApp.ps1 -OutlookConnectionId <id> -WhatIf`, then for real, then again (all `unchanged`); confirm `id-mygarage-intake` holds the `Email.Ingest` role on `MyGarage Azure API` and the trigger runs on the 5-minute interval
- [ ] T055 [US2] Run the deferred end-to-end walkthrough (quickstart step 7) against the deployed environment: ask the owner to sign in to the web app, forward a service invoice PDF from that user's address to `mygarage-intake@outlook.com`, confirm the Logic App run shows `202`, a draft and a notification appear, open the draft and view the PDF in Chrome (PDFs must display, not be sandboxed), accept it, and then send mail from an address that matches no user and confirm `202` and nothing visible for any user
- [ ] T056 [US2] Update `specs/007-email-ingestion-api/tasks.md`: change the "Deferred to the next spec" note on the manual quickstart walkthrough to say it was completed in spec 008 (T055)

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T057 [P] Update the root `README.md` and `docs/README.md` with a short CI/CD section: the `verify` check, the deployment flow, the deployed addresses pattern, and links to `infra/README.md`; update `tests/README.md` with the note that all suites also run on Linux in CI and require Docker
- [X] T058 [P] Add to `infra/README.md` a "Follow-ups" list recording the deferred items so they are not lost: narrow the pipeline identity (Contributor on `rg-apps` and `rg-platform` resources only, custom role for joining `cae-shared` and creating one database on `accpsqlshared`, User Access Administrator limited to `rg-apps`, the storage account, and `aif-shared-acc`), queue-driven scale rule, registry image retention, custom domain, staging environment, Application Insights, Defender for Storage
- [ ] T059 Run the complete quickstart.md end to end (steps 1-8) against the deployed environment and record each result in the pull request; confirm cost checks (`minReplicas` 0, web `maxReplicas` 1, no Defender plan)
- [ ] T060 Final gates: `dotnet build MyGarage.slnx` with zero warnings, every test project passes (`dotnet test`), `az bicep build` and `az bicep lint` clean, `Test-NoSecrets.ps1` passes, migrations still apply to an empty database; run `./infra/github/Initialize-GitHub.ps1 -RestrictEnvironmentToMain` so only `main` can deploy to `production`; open the pull request for `008-github-cicd-pipeline` (never commit to `main` directly) and leave the merge to the owner after review against the constitution

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies
- **Foundational (Phase 2)**: needs Setup; T004 and T005 are already done. Blocks US2 (deploy needs probes, token database access, and the web credential); does not block US1
- **US1 (Phase 3)**: can start right after Setup (it only needs the existing code); needs the Foundational application changes only so its first Linux run covers them
- **US3 (Phase 4)**: needs Phase 2 only for ordering; T022-T024 are independent files; T025 needs T022-T024; T028 needs T025-T027 and an owner sign-in
- **US2 (Phase 5)**: needs US1 (verify workflow), US3 (identities, database, registrations, pipeline identity), and Phase 2
- **US4 (Phase 6)**: needs US2 deployed (T044-T046); T041-T043 can start after US1
- **US5 (Phase 7)**: needs US2 (`deploy.yml`)
- **Logic App (Phase 8)**: needs US2 deployed and US3 (registrations); independent of US4 and US5
- **Polish (Phase 9)**: needs everything it documents

### Within a phase

- Tests are written and seen failing before their implementation (T006 before T007, T008 before T009)
- Bicep modules before `main.bicep` before `main.bicepparam` before the what-if (T029-T034)
- Pin actions (T015) before the first workflow run (T017); `Initialize-GitHub.ps1` only after `verify` has reported once (T021)

### Story completion order

US1 -> (Phase 2 + US3 in parallel) -> US2 -> US4, US5, and the Logic App in parallel -> Polish.

## Parallel opportunities

- Setup: T002, T003 together
- Foundational: T006 and T008 (write tests) together, then T007 and T009 together
- US1: T011, T014, T020 together; T012-T013 follow each other
- US3: T022, T023, T024 together; T026 alongside T025
- US2: T029, T030 together; T035 alongside T031-T033
- After US2: US4 (T041, T043), US5 (T047-T049), and the Logic App (T051, T053) together

### Parallel example: Phase 2 and US3 start

```text
Task: "T006 EntraDatabaseAuthenticationTests in tests/MyGarage.Api.Tests/Common/"
Task: "T008 Federated-credential tests in tests/MyGarage.Web.Tests/"
Task: "T022 Extend infra/entra/Initialize-Entra.ps1"
Task: "T023 infra/bicep/modules/identities.bicep"
Task: "T024 infra/bicep/modules/platform-database.bicep"
```

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 Setup.
2. Phase 3 US1 (CI plus branch protection). **Stop and validate**: quickstart steps 2-3. Every change is now verified on Linux and `main` is protected; this alone delivers the merge gates the constitution promises.
3. Phase 2 can run before or alongside.

### Incremental delivery

1. US1 -> validated CI (MVP).
2. Phase 2 + US3 -> the application and Azure are ready.
3. US2 -> first automatic deployment and the failure rehearsals.
4. US4, US5, and the Logic App -> hardening, release control, and the email walkthrough.
5. Polish -> docs, follow-ups, full quickstart, pull request.

## Notes

- [P] tasks touch different files and have no dependency on an incomplete task
- Commit after each task or logical group with Conventional Commits (`feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`, `chore`), concise imperative subjects, no trailing period
- Owner-involved tasks (T021, T028, T034, T037, T044, T054, T055) require the owner to be signed in to the relevant service; ask first, then continue
- Never edit an applied migration; this feature adds none
