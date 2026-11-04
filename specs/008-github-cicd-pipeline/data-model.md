# Data Model: GitHub CI/CD Pipeline

No application data model changes: no table, migration, DTO, or API contract is added or altered. The "data" of this feature is the configuration and identity model that connects GitHub, Azure, and the running applications. The key entities of the spec map as follows.

## Entities

### Resource mapping (spec: *Resource mapping*)

| Component | Resource | Group | Managed by |
|---|---|---|---|
| Container Apps environment | `cae-shared` | `rg-apps` | existing, referenced only |
| API app | `ca-api-mygarage` | `rg-apps` | pipeline (Bicep) |
| Web app | `ca-web-mygarage` | `rg-apps` | pipeline (Bicep) |
| API identity | `id-mygarage-api` | `rg-apps` | Bicep (`identities`), deployed once early by the setup script |
| Web identity | `id-mygarage-web` | `rg-apps` | Bicep (`identities`), deployed once early by the setup script |
| Logic App identity | `id-mygarage-intake` | `rg-apps` | `New-IntakeLogicApp.ps1` (not pipeline) |
| Registry | `acccrshared` | `rg-platform` | existing, referenced only |
| Registry pull identity | `id-shared-acrpull` | `rg-platform` | existing, referenced only |
| Database server | `accpsqlshared` | `rg-platform` | existing, referenced only |
| Database | `mygarage` | on the server | Bicep module (pipeline), also deployed once by the setup script |
| Database principal | Entra principal `id-mygarage-api`, owner of `mygarage` | server | one-time script |
| Storage account | `stmygarage<unique>` | `rg-platform` | pipeline (Bicep) |
| Blob containers | `vehicle-images`, `email-intake`, `vehicle-documents` | storage account | created by the API at start-up (existing behaviour) |
| Extraction service | `aif-shared-acc` deployment `gpt-4.1-mini` | `rg-platform` | existing, referenced only; role assignment by Bicep |
| Logging | `law-shared` via `cae-shared` | `rg-apps` | existing |
| Logic App | `logic-mygarage-intake` | `rg-apps` | `New-IntakeLogicApp.ps1` (not pipeline) |
| Outlook connection | `Microsoft.Web/connections` resource `con-mygarage-outlook`, created by the Logic App script, authorised by the owner | region `northcentralus` | owner |
| Entra registrations | `MyGarage Azure API`, `MyGarage Azure Web` | tenant | `Initialize-Entra.ps1` (extended) |

### Role assignments created

| Principal | Role | Scope | Created by |
|---|---|---|---|
| `id-mygarage-api` | Storage Blob Data Contributor | storage account | pipeline (Bicep) |
| `id-mygarage-api` | Cognitive Services OpenAI User | `aif-shared-acc` | pipeline (Bicep) |
| `id-mygarage-intake` | app role `Email.Ingest` | `MyGarage Azure API` service principal | `New-IntakeLogicApp.ps1` |
| `mygarage-github-oidc` | Contributor, User Access Administrator (temporary) | subscription | `Initialize-Azure.ps1` |
| `mygarage-github-oidc` | AcrPush | `acccrshared` | `Initialize-Azure.ps1` |
| `MyGarage Azure Web` | federated credential trusting `id-mygarage-web` (no client secret) | app registration | `Initialize-Entra.ps1` |

### Pipeline identity (spec: *Pipeline identity*)

| Attribute | Value |
|---|---|
| App registration | `mygarage-github-oidc` (service principal created with it) |
| Trust | issuer `https://token.actions.githubusercontent.com`; subjects `<prefix>:environment:production` and `<prefix>:ref:refs/heads/main`; audience `api://AzureADTokenExchange`; `<prefix>` = `repo:TK11703@174497/my-garage@1439821961`, read from GitHub by the script |
| Rights | see the role table above; the subscription-wide rights are a documented, temporary exception to FR-019 |
| Lifetime | tokens are issued per workflow run (minutes); nothing stored |

Relationship: pipeline identity -> (deploys) -> API and web apps -> (run as) `id-mygarage-api` / `id-mygarage-web` plus `id-shared-acrpull`.

### Environment configuration (spec: *Environment configuration*)

Held in the GitHub environment `production`; see [contracts/environment-config.md](contracts/environment-config.md). Lifecycle: created by `Initialize-Azure.ps1` and the extended `Initialize-Entra.ps1`, read by the workflow, passed to Bicep as parameters, materialised as Container App environment settings. There are no secrets: no database password and no web client secret (the web app uses a federated credential on `id-mygarage-web`).

### Verification run (spec: *Verification run*)

| Field | Meaning |
|---|---|
| Commit and trigger | pull request head, push to `main`, or manual dispatch |
| Jobs | `changes`, `build-test`, `bicep`, `verify` (the required gate) |
| Outcome | `success`, `failure`, or `skipped` per job; `verify` is `success` only when every needed job is `success` or `skipped` |
| Evidence | TRX files and a build log as artifacts, a failure summary in the run's summary page |

State transitions: queued -> running -> success | failure | cancelled (superseded by a newer commit on the same pull request).

### Deployment (spec: *Deployment*)

| Field | Meaning |
|---|---|
| Commit and image tag | `sha-<commit>`; one tag for both images |
| Environment | `production` (GitHub environment, optional required reviewer) |
| Revisions | one new revision per app; the previous revision stays active until the new one is ready |
| Outcome | `succeeded` (both apps healthy on the new tag), `failed` (previous revision still serving), `cancelled` |

State transitions: waiting (optional approval) -> verifying -> building images -> what-if -> deploying API -> deploying web -> health check -> succeeded | failed.

## Validation rules

- The image tag must be `sha-` plus a full 40-character commit SHA; anything else is rejected before deployment.
- A deployment never runs for a commit whose `verify` did not succeed.
- Required Bicep parameters that are empty (for example the web client id) fail the deployment before any resource changes.
- The deployment only runs in incremental mode; any template change that would delete a resource is a review item in the what-if output.
- Setup scripts skip an object that already exists with the desired value and report `unchanged`.
