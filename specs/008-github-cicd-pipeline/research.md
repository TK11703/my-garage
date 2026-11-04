# Research: GitHub CI/CD Pipeline

Findings come from read-only inspection of the Azure subscription (`Azure Personal Subscription`, tenant `c1fae319-f9bf-4708-abe1-5b7c9f6ffc81`), the GitHub repository `TK11703/my-garage`, and the code. Nothing was changed. Revised 2026-11-03 with the owner's corrections (applications in `rg-apps`, storage in `rg-platform`, three identities, new Entra registrations, subscription-level pipeline rights for now, Entra token database access in Azure, no Defender).

## Inventory: existing resources and how they map (FR-015)

| Application component | Existing resource | Details found | Verdict |
|---|---|---|---|
| Container hosting (web, API) | Container Apps environment `cae-shared`, `rg-apps`, North Central US | Consumption workload profile only, no virtual network, default domain `victoriouspond-0f22d154.northcentralus.azurecontainerapps.io`, logs to `law-shared`. Four sibling apps already run in it at 0.25 vCPU / 0.5 GiB with minimum replicas 0. | Reuse; the apps are deployed into `rg-apps` |
| Image registry | `acccrshared` (Basic), `rg-platform` | Admin user off, public access on. Pull identity `id-shared-acrpull` (AcrPull) is attached to every sibling app. `gisr-github-oidc` holds AcrPush. | Reuse; the new pipeline identity needs AcrPush (a data-plane right Contributor does not include) |
| Relational database | PostgreSQL flexible server `accpsqlshared`, `rg-platform` | v16, Burstable `Standard_B1ms`, 32 GB, `max_connections` 50, Entra and password authentication both enabled, public access with an "Azure services" firewall rule, extension allow-list `pgcrypto,vector` (the schema needs no extension). Databases: `tripplanner`, `notepal`, `Gisr`. Entra admins: the owner and `gisr-github-oidc`. | Reuse; add database `mygarage` |
| Private file storage | none shared (each sibling has its own account) | No account for My Garage. | **Gap: create one Standard LRS account in `rg-platform`** |
| Document extraction | AI Foundry account `aif-shared-acc`, `rg-platform` | Deployments `gpt-4.1`, `gpt-4.1-mini`, `text-embedding-3-large`. The extractor calls `{endpoint}/openai/v1/responses` with token scope `https://ai.azure.com/.default`. | Reuse with deployment `gpt-4.1-mini`; grant the API identity *Cognitive Services OpenAI User* |
| Monitoring | `law-shared` (Log Analytics), `rg-apps` | The environment already streams console logs to it. | Reuse; no new resource |
| Identities | siblings keep one user-assigned identity per app in `rg-apps` | None for My Garage. | **Gap: create `id-mygarage-api`, `id-mygarage-web`, `id-mygarage-intake` in `rg-apps`** |
| Entra registrations | `MyGarage API` and `MyGarage Web` exist (local development, localhost redirect URIs) | Created by `infra/entra/Initialize-Entra.ps1`. | Leave untouched; **create separate registrations for Azure** |
| GitHub-to-Azure identity | `gisr-github-oidc` (subscription Contributor + User Access Administrator) | Federated credentials for another repository. | Not reused; create `mygarage-github-oidc` |
| Email Logic App | `logic-trip-planner-email-relay` (Consumption, user-assigned identity, Outlook connection) | A working precedent. Not ours to reuse. | New Logic App and a connection created by the script |

Other facts used below: the repository is public, `main` has no branch protection, there are no GitHub environments, and the repository's OIDC subject claim uses GitHub's immutable form (`repo:TK11703@174497/my-garage@1439821961`).

## Decisions

### R1: Hosting and scaling

- **Decision**: Two Container Apps, `ca-api-mygarage` and `ca-web-mygarage`, in `rg-apps` (region `northcentralus`) using `cae-shared`, single-revision mode, external ingress on port 8080, 0.25 vCPU / 0.5 GiB, minimum replicas 0, HTTP concurrency scale rule (20 requests). API maximum 2 replicas, cooldown 300 s. Web maximum **1** replica.
- **Rationale**: Matches the sibling apps and the cost goal. The web app deliberately invalidates sessions created by another process instance (`SessionValidation.CurrentInstance`) and keeps Blazor circuits in memory, so it must not run more than one replica; sessions restart after a scale to zero and Entra single sign-on makes that a silent redirect. The API must be reachable from outside the environment because the Logic App calls it; every route still requires a valid Entra token.
- **Alternatives**: Web with sticky sessions and several replicas (rejected: the session-instance check would sign users out on every cross-replica hop). Internal-only API ingress (rejected: the Logic App is outside the environment). Always-on minimum replica (rejected: cost).
- **Known trade-off**: the API runs background services (submission processor, draft expiry, orphan sweep) that run only while a replica is up. An intake call wakes the API and the 300 s cooldown keeps it up long enough to process the email; if it stops first, the next request resumes the queue. A queue-driven scale rule is out of scope.

### R2: Container images without a Dockerfile

- **Decision**: Build with `dotnet publish -t:PublishContainer` for `MyGarage.Api` and `MyGarage.Web`, repositories `mygarage/api` and `mygarage/web`, tag `sha-<full commit sha>`, pushed to `acccrshared.azurecr.io` after `az acr login`. Build once in `build-images` and deploy the exact tag.
- **Rationale**: No new files to maintain, the SDK image is non-root and listens on 8080, and the tag is immutable per commit, which gives rollback by tag (FR-008, FR-012).
- **Alternatives**: Hand-written Dockerfiles (more files, same result). `az acr build` (billed registry tasks).

### R3: Verification workflow

- **Decision**: One reusable workflow `verify.yml` with jobs `changes`, `build-test`, `bicep`, and a final always-running gate job named `verify` that passes only when every needed job passed or was skipped. `ci.yml` calls it for pull requests; `deploy.yml` calls it for pushes to `main` and manual dispatch.
- **Rationale**: One always-reporting required check solves "documentation-only PR can still merge" (FR-028) without path filters that leave a required check pending. A per-pull-request concurrency group with `cancel-in-progress` covers FR-005. The Testcontainers and AppHost suites need Docker, which `ubuntu-latest` provides; a missing runtime fails loudly.
- **Migrations check (FR-003)**: the existing `MyGarage.Database.Tests` apply every migration to an empty Testcontainers PostgreSQL and verify ledger checksum behaviour. A test is added only if that coverage is found missing.
- **Linux test adjustments (owner decision)**: the suites were written on Windows. Failures that appear on the first Linux runs (path separators, file-name case, line endings, timing) are fixed in the tests by making them OS-neutral, never by skipping or deleting them, and never by changing application behaviour. These fixes are explicit tasks and are expected.
- **Alternatives**: `paths-ignore` (rejected: required check never reported). Windows runner (rejected: no Linux containers). A third-party test reporter package (rejected: new dependency; a small script turns TRX into a step summary and TRX is uploaded as an artifact).

### R4: Deployment workflow

- **Decision**: `deploy.yml` triggers on push to `main` and `workflow_dispatch` (inputs `commit` and `dryRun`). Jobs: `verify` -> `build-images` (skips when the tag already exists) -> `deploy` (environment `production`): `azure/login` with OIDC, `az deployment group what-if`, then `az deployment group create -g rg-apps --mode Incremental`, then `Test-Health.ps1`. Concurrency group `deploy-production` with `cancel-in-progress: false` so deployments run one at a time and the environment ends on the latest commit (FR-011).
- **Health and rollback behaviour**: single-revision mode shifts traffic only after the new revision's readiness probe passes. The API applies migrations in a hosted service that finishes before it starts listening, so a failing migration leaves the new revision unready and the previous revision keeps serving (FR-009, FR-010). `Test-Health.ps1` also calls `/health` on both public addresses and confirms the active revision carries the new tag.
- **Order**: the web app depends on the API's address, so the API is created or updated first (a normal Bicep dependency).
- **Manual rollback**: dispatch with an earlier commit; it is verified again, its images are reused if present or rebuilt, and the same deployment runs. Because migrations are forward-only, rollback is supported only across releases whose schema change is backward compatible; the runbook states this.
- **Alternatives**: `azd` (rejected: the owner asked for hand-written Bicep). Separate stages per app (rejected: slower, no benefit at this size).

### R5: Pipeline identity (owner decision: broad rights for now)

- **Decision**: One app registration and service principal `mygarage-github-oidc` with two federated credentials (issuer `https://token.actions.githubusercontent.com`): subject `<prefix>:environment:production` for deployment and `<prefix>:ref:refs/heads/main` for the image build job. The prefix is read from GitHub by the setup script. Role assignments:

  | Scope | Role | Why |
  |---|---|---|
  | subscription | Contributor | create and update resources in `rg-apps` and `rg-platform` |
  | subscription | User Access Administrator | the deployment assigns roles (blob access, extraction access) |
  | `acccrshared` | AcrPush | push images (data-plane right that Contributor does not include) |

- **Rationale**: Removes permission problems while the pipeline is brought up. It still stores no Azure secret, and its trust is limited to this repository and to the `production` environment and the `main` branch, so a fork, another repository, another branch, or another environment cannot obtain a token.
- **Documented deviation**: FR-019 asks for rights scoped to the resources the application uses. The owner chose subscription-wide Contributor and User Access Administrator for now. It is recorded in Complexity Tracking and in the spec (FR-019 exception). The intended tightening (Contributor on `rg-apps` and `rg-platform` resources only, custom role for joining the environment and creating one database, User Access Administrator limited to `rg-apps`, the storage account, and `aif-shared-acc`) is a follow-up task, not part of this feature.
- **Alternatives**: Reuse `gisr-github-oidc` (rejected: other repository's trust). A client secret (rejected by FR-018).

### R6: Bicep structure and ownership

- **Decision**: `infra/bicep/main.bicep` targets resource group `rg-apps` and composes modules: `identities` (rg-apps), `storage` (scope `rg-platform`), `platform-database` (scope `rg-platform`, database `mygarage` on the existing server), `ai-access` (scope `rg-platform`, *Cognitive Services OpenAI User* for the API identity on `aif-shared-acc`), `container-apps` (rg-apps). Existing resources are referenced with `existing` and never declared as managed, so incremental deployment cannot alter or delete them (FR-014, FR-017). Parameters come from `main.bicepparam` using `readEnvironmentVariable`, so no value file with secrets exists.
- **Rationale**: Secure parameters are never written to disk, and the same file works locally for the owner.
- **Alternatives**: Subscription-scope template (rejected: unnecessary for this layout). Terraform (rejected: Bicep requested).

### R7: The three identities

- **Decision**:
  - `id-mygarage-api` (user-assigned, `rg-apps`): used by the API for blob storage (*Storage Blob Data Contributor* on the new account), extraction (*Cognitive Services OpenAI User*), and **database access** (it is a PostgreSQL Entra principal, R8).
  - `id-mygarage-web` (user-assigned, `rg-apps`): attached to the web app and used as its `AZURE_CLIENT_ID`. It has no Azure role assignments (the web calls only the API and Entra, and registry pull uses `id-shared-acrpull`); its purpose is to be the web application's **federated credential** for Entra sign-in and token requests (R17), so no client secret exists.
  - `id-mygarage-intake` (user-assigned, `rg-apps`): the Logic App's identity, holds the `Email.Ingest` app role on the Azure API registration.
- **Ownership**: the first two are defined in `infra/bicep/modules/identities.bicep` (included by `main.bicep`, so the pipeline keeps them correct) and are also deployed once by `Initialize-Azure.ps1`, because the database principal for `id-mygarage-api` must exist before the first API revision starts. The third is created by `New-IntakeLogicApp.ps1`, because the Logic App stays outside the pipeline.
- **Alternatives**: System-assigned identities (rejected: the owner asked for named identities and a pre-existing principal is needed before the first deployment).

### R8: Database access (owner decision: Entra token in Azure, password locally)

- **Decision**:
  - **Azure**: the API signs in to PostgreSQL with a Microsoft Entra access token (scope `https://ossrdbms-aad.database.windows.net/.default`) for `id-mygarage-api`; the connection string has no password: `Host=accpsqlshared.postgres.database.azure.com;Database=mygarage;Username=id-mygarage-api;SSL Mode=Require;Maximum Pool Size=10`, as a plain Container App setting (it holds no secret; the Bicep linter requires `@secure()` values for Container App secrets). The app setting `Database__UseEntraAuthentication=true` switches the behaviour on.
  - **Local**: unchanged; the AppHost-provided connection string with a password.
  - **Code change** (small, in `MyGarage.Api` only): when the setting is true, the Npgsql data source is configured with a periodic password provider that gets tokens from `DefaultAzureCredential` (it honours `AZURE_CLIENT_ID`, so it picks `id-mygarage-api`), refreshing well before expiry. It lives in `MyGarage.Api/Common/` and is registered through the `configureDataSourceBuilder` hook of `AddNpgsqlDataSource`. No new package (Npgsql and Azure.Core are already referenced). The migration runner and every repository keep using the same data source.
  - **One-time setup**: `Initialize-Azure.ps1` deploys the identities and the database module, then, connected as the owner (an Entra admin of the server, using an access token), creates the principal with `pgaadauth_create_principal('id-mygarage-api', false, false)`, makes it owner of database `mygarage` (PostgreSQL 16 does not let ordinary roles create objects in `public`), and revokes `PUBLIC` connect on the database.
- **Rationale**: No database password exists in Azure, in GitHub, or in a Container App secret. The token provider is the supported Npgsql pattern, and the pool cap of 10 protects the shared server's 50 connections.
- **Tests (first)**: a unit test in `MyGarage.Api.Tests/Common` uses a fake `TokenCredential` to prove the password provider asks for the right scope and returns the token, and that nothing changes when the setting is false. The real flow is proven by the quickstart.
- **Alternatives**: A password in a Container App secret (the sibling pattern; rejected by the owner). Letting the pipeline identity create the principal (rejected: it would need to be a database admin).

### R9: Secrets and configuration

- **Decision**: Environment `production` holds variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `API_ENTRA_CLIENT_ID`, `API_ENTRA_SCOPE`, `WEB_ENTRA_CLIENT_ID`, `EXTRACTION_ENDPOINT`, `EXTRACTION_DEPLOYMENT`. There are **no secrets**: no database password and, with the federated credential (R17), no web client secret. Exact names are in [contracts/environment-config.md](contracts/environment-config.md).
- **Alternatives**: Key Vault references (a new resource and cost, and nothing left to store).

### R10: Application change for probes (implemented in this feature)

- **Decision**: `MapDefaultEndpoints` in `MyGarage.ServiceDefaults` maps `/health` (all checks) and `/alive` (liveness only) for every environment, still anonymous, returning only the plain status word. Tests were written first (Production-environment host returns 200 on both without a token and the body is only the status word) in `MyGarage.Api.Tests` and `MyGarage.Web.Tests`. Probes: startup and liveness on `/alive`, readiness on `/health`.
- **Rationale**: Without it the platform has no meaningful probe and FR-010 cannot be met. `AddNpgsqlDataSource` and the Aspire blob client add their health checks, so `/health` also proves the database and storage are reachable.
- **Other behaviours confirmed**: `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` makes redirect URIs use `https` behind ingress; the web finds the API through `services__api__https__0`, which the existing service-discovery client reads.

### R11: Entra registrations for Azure (owner decision: new registrations)

- **Decision**: Create separate registrations **`MyGarage Azure API`** and **`MyGarage Azure Web`** by extending `infra/entra/Initialize-Entra.ps1` with `-NamePrefix 'MyGarage Azure'`, a new `-WebBaseUri https://ca-web-mygarage.<default domain of cae-shared>` (replacing the localhost redirect URIs), and `-GitHubEnvironment production` (sending the client ids and scope to GitHub instead of the AppHost user-secrets), and `-ManagedIdentityPrincipalId <id-mygarage-web principal id>` (adding the federated credential to the web registration and creating no client secret, R17). Everything else (single tenant, Application ID URI, `access_as_user` scope, `Email.Ingest` app role, `email` optional claim, token version 2, delegated permission and admin consent) is the same logic, so the existing local registrations and defaults are unchanged. `Initialize-Azure.ps1` calls it.
- **Rationale**: Deployed and local environments get independent registrations, secrets, redirect URIs, and consent. The email-intake role is assigned on the Azure API registration.

### R12: Logic App

- **Decision**: A Consumption Logic App `logic-mygarage-intake` in `rg-apps` (`northcentralus`), created by `New-IntakeLogicApp.ps1` from `workflow.definition.json`, using user-assigned identity `id-mygarage-intake` (the script creates the identity when missing). Components:
  - **Trigger**: Outlook.com "When a new email arrives (V3)", folder Inbox, includes attachments, recurrence 5 minutes (`PollIntervalMinutes`), on the connection `con-mygarage-outlook` to `mygarage-intake@outlook.com` (`-OutlookConnectionName`; the script creates it from `outlook-connection.bicep` when missing, never redeploys it once it exists, and never consents to it: the owner authorises it once in the portal).
  - **Identity grant**: the script assigns `id-mygarage-intake`'s service principal the `Email.Ingest` app role on `MyGarage Azure API` through Microsoft Graph (`az rest`), skipping when already granted. The owner running it holds the needed Entra rights.
  - **Call**: HTTP POST to `https://<api fqdn>/api/intake/emails`, authentication type Managed identity, identity `id-mygarage-intake`, audience `api://<Azure API client id>`.
  - **Mapping, retry, failure handling**: [contracts/logic-app-intake.md](contracts/logic-app-intake.md), following the contract in `infra/README.md`.
- **Rationale**: Matches the sibling precedent, costs well under a dollar a month at 5-minute polling, and sits outside the pipeline as requested. It needs the deployed API address, so it is created after the first deployment.
- **Open point**: confirm that "Html to text" is a built-in Consumption action in the region; otherwise strip tags with an expression.

### R13: One-time GitHub setup

- **Decision**: `Initialize-Azure.ps1` creates GitHub environment `production` and sets its variables and the secret. `Initialize-GitHub.ps1` (GitHub CLI, owner authenticated) adds an optional required reviewer, creates a repository ruleset on `main` requiring a pull request and the `verify` check and blocking force pushes and deletion, and sets the default workflow token to read-only. Run it after the first pipeline run has reported `verify`, because GitHub can only require a check it has seen.

### R14: Action pinning and supply chain

- **Decision**: All `uses:` references are pinned to full commit SHAs with the version in a trailing comment, workflows declare least-privilege `permissions` (default `contents: read`; `id-token: write` only on jobs that sign in to Azure), and Dependabot updates the pins weekly (FR-027).

### R15: Cost summary

| Item | Expected monthly cost |
|---|---|
| Container Apps (scale to zero, small usage) | within the free monthly grant at this scale; cents beyond |
| Registry, environment, workspace, database server, AI account | already paid for; this app adds storage and a few images |
| Storage account (Standard LRS, light use) | well under $1 |
| Logic App Consumption (5-minute polling) | under $1 |
| Defender for Storage | **not enabled** (owner decision; about $10 a month per account). The recommendation in `infra/README.md` stays as a future option and no parameter exists for it |
| GitHub Actions on a public repository | free |

### R16: Out of scope and deferred

- Narrowing the pipeline identity's rights (R5), queue-driven scale rule, registry image retention, custom domain, staging environment, Application Insights, and Defender for Storage.

### R17: Web sign-in with a federated credential (owner decision: no client secret)

- **Decision**: In Azure, the web application proves its identity to Entra with a signed assertion from its user-assigned managed identity `id-mygarage-web`, not a client secret.
  - **Entra**: the `MyGarage Azure Web` registration gets a federated identity credential `mygarage-web-managed-identity`: issuer `https://login.microsoftonline.com/<tenant id>/v2.0`, subject the managed identity's object (principal) id, audience `api://AzureADTokenExchange`. `Initialize-Entra.ps1 -ManagedIdentityPrincipalId` adds it and creates no secret. The identity is deployed first so its principal id exists (setup order in [contracts/azure-setup.md](contracts/azure-setup.md)).
  - **Code change** (small, in `MyGarage.Web` only): `EntraWebSettings.WebSection` gains `ManagedIdentityClientId`. When it is set, `AddEntraAuthentication` configures `MicrosoftIdentityOptions.ClientCredentials` with one `CredentialDescription` whose `SourceType` is `SignedAssertionFromManagedIdentity` and `ManagedIdentityClientId` is that value, instead of `ClientSecret`. The same options feed `EnableTokenAcquisitionToCallDownstreamApi`, so both sign-in and the on-behalf token requests to the API use the federated credential. `Load` requires a client secret only when no managed identity client id is configured, so local development (secret from the AppHost user-secrets) is unchanged. Microsoft.Identity.Web 4.16.1 already supports this source type, so there is no new package.
  - **Deployment**: the web Container App gets `AzureAd__Web__ManagedIdentityClientId` (client id of `id-mygarage-web`) and no secret, so the web app needs no Container App secret and the GitHub environment holds no secrets at all.
- **Tests (first)**: in `MyGarage.Web.Tests/Common` (a) `Load` succeeds with a managed identity client id and no secret, and still fails when neither is set; (b) a host configured with only the managed identity client id resolves `MicrosoftIdentityOptions` for the OpenID Connect scheme with exactly one credential of source type `SignedAssertionFromManagedIdentity` and no client secret; (c) a host configured with a secret and no managed identity keeps the secret credential. The real exchange is proven by signing in to the deployed app (quickstart step 4).
- **Rationale**: Removes the last stored credential, so there is nothing to rotate or leak, and uses the identity the owner already wants for the web app. It needs only an Entra federated credential, which has no cost.
- **Risks**: the federated credential trusts one identity, so recreating `id-mygarage-web` (new principal id) requires re-running `Initialize-Entra.ps1`, which is idempotent. A managed identity assertion is only available inside Azure, so this path can be exercised only in the deployed environment; the unit tests prove the configuration, the quickstart proves the exchange.
- **Alternatives**: Client secret in a Container App secret (rejected by the owner). Certificate credential (needs a certificate and rotation).
