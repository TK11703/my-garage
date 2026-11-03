# Infrastructure

Use this folder for infrastructure-as-code templates, environment configuration, and scripts that provision and deploy the application's supporting resources.

Never commit secrets or credentials; reference secure configuration sources instead.

| Folder | Contents |
|---|---|
| [entra/](entra/README.md) | Script and instructions that register the web app and API in a Microsoft Entra tenant and populate the AppHost user-secrets (or, for the deployed environment, GitHub variables and a federated credential) |
| [azure/](azure/README.md) | One-time, repeatable Azure and GitHub preparation for the pipeline (`Initialize-Azure.ps1`) |
| [github/](github/README.md) | Repository settings the pipeline relies on: branch ruleset, workflow token, environment reviewer (`Initialize-GitHub.ps1`) |
| [bicep/](bicep/) | Bicep templates the pipeline deploys into `rg-apps` and `rg-platform` |

## Azure resource mapping

My Garage runs on shared resources that already exist in the subscription (region `northcentralus`). The pipeline never creates, replaces, or deletes a shared resource; it only adds the components marked *new*.

| Component | Resource | Group | Status |
|---|---|---|---|
| Container hosting | Container Apps environment `cae-shared` | `rg-apps` | existing, reused |
| Web app and API | Container Apps `ca-web-mygarage`, `ca-api-mygarage` (0.25 vCPU / 0.5 GiB, scale to zero) | `rg-apps` | new |
| Identities | `id-mygarage-api`, `id-mygarage-web`, `id-mygarage-intake` | `rg-apps` | new |
| Image registry | `acccrshared` (repositories `mygarage/api`, `mygarage/web`) and pull identity `id-shared-acrpull` | `rg-platform` | existing, reused |
| Database | PostgreSQL flexible server `accpsqlshared`, database `mygarage` | `rg-platform` | server existing, database new |
| File storage | Storage account `stmygarage<unique>` | `rg-platform` | new (no shared account existed) |
| Document extraction | AI Foundry account `aif-shared-acc`, deployment `gpt-4.1-mini` | `rg-platform` | existing, reused |
| Logging | Log Analytics workspace `law-shared` through `cae-shared` | `rg-apps` | existing, reused |
| Email intake | Logic App `logic-mygarage-intake` and the owner's Outlook connection | `rg-apps` | new, created by script (not the pipeline) |
| Sign-in | Entra registrations `MyGarage Azure API` and `MyGarage Azure Web` | tenant | new |
| Pipeline | App registration `mygarage-github-oidc`, GitHub environment `production` | tenant, GitHub | new |

## CI/CD pipeline

Two GitHub Actions workflows in `.github/workflows/` verify and deploy the app. Their design is in `specs/008-github-cicd-pipeline/`.

| Workflow | Runs on | Does |
|---|---|---|
| `ci.yml` (with the reusable `verify.yml`) | every pull request to `main` | Builds with warnings as errors, runs all four test projects (Docker is available on the runner), checks the Bicep, scans for secrets, and reports the required check `verify`. Documentation-only changes skip the heavy jobs but still report `verify` |
| `deploy.yml` | push to `main`, or a manual run | Verifies the commit, publishes `mygarage/api` and `mygarage/web` to `acccrshared` once per commit (tag `sha-<commit>`), waits for the owner's approval of the `production` environment, deploys `bicep/main.bicep` into `rg-apps` (incremental, never deletes), and checks both apps on `/health` |

GitHub signs in to Azure with a federated credential for `mygarage-github-oidc`; no Azure secret is stored. The GitHub environment `production` holds only variables (no secrets).

### First-time setup

1. `./infra/azure/Initialize-Azure.ps1` (see [azure/README.md](azure/README.md))
2. Merge the first pull request, let `ci.yml` report `verify`, then run `./infra/github/Initialize-GitHub.ps1` (see [github/README.md](github/README.md))
3. After the first deployment, create the Logic App with [logic-app/New-IntakeLogicApp.ps1](logic-app/README.md)

### Deploying, reading a run, and rolling back

- A merge to `main` starts `deploy.yml`. Open the run, approve the pending `production` deployment, and read the run summary: commit, image tag, what-if counts, addresses, health outcome.
- To redeploy or roll back, run `deploy.yml` manually (Actions > deploy > Run workflow) with `commit` set to a full commit SHA that is on `main`; use `dryRun` to stop after the what-if. The commit is verified again, its images are reused when the tag exists in the registry and rebuilt otherwise, and the same checks run.
- Rollback is code-only: migrations are forward-only and run when the API starts. Keep every schema change backward compatible with the previous release so an earlier commit still works against the newer schema.
- A new revision receives traffic only after it is ready. If migrations fail or `/health` fails, the previous revision keeps serving and the run fails.
- See what is deployed: `az containerapp show -n ca-api-mygarage -g rg-apps --query "properties.template.containers[0].image"` (the tag is `sha-<commit>`), or the `ReleaseId` setting.
- Failure notifications: GitHub notifies the person who triggered or last changed the workflow according to their notification settings. Turn on *Settings > Notifications > Actions* and choose to be notified of failed workflows.
- The API's background services (email processing, draft expiry, orphan sweep) run only while a replica is up. An intake call wakes the API, and the 300 second cooldown keeps it up long enough to process the email; if it stops first, the next request resumes the queue.

## Follow-ups

Deferred on purpose; none blocks the pipeline.

- Narrow the pipeline identity: it holds Contributor and User Access Administrator on the whole subscription for now. Target: Contributor on `rg-apps` and `rg-platform` resources only, a custom role for joining `cae-shared` and creating one database on `accpsqlshared`, and User Access Administrator limited to `rg-apps`, the storage account, and `aif-shared-acc`.
- Queue-driven scale rule for the API, registry image retention, a custom domain, a staging environment, Application Insights, and Defender for Storage.

## Azure Storage for vehicle pictures

Locally the AppHost runs the Azurite emulator, so nothing needs provisioning. In Azure the pipeline creates the storage account from `bicep/modules/storage.bicep` (in `rg-platform`) configured as follows:

- Blob container `vehicle-images`, **private** (anonymous access disabled on the account and the container). The API creates the container at startup when it has permission; infrastructure may create it instead.
- Shared-key access disabled and HTTPS only. The API authenticates with its **managed identity** (`DefaultAzureCredential`), which needs the *Storage Blob Data Contributor* role on the account (or container).
- Configuration read by the Aspire Azure Blob client: `ConnectionStrings:blobs` (a service URI such as `https://<account>.blob.core.windows.net/`) or `Aspire:Azure:Storage:Blobs:ServiceUri`.
- No SAS tokens or storage URLs are ever returned to browsers; pictures are streamed by the API through the web app.

The PostgreSQL database is expected as connection string `ConnectionStrings:garagedb`; the API applies its migrations at startup. In Azure the database `mygarage` lives on the shared server and the API connects as its managed identity: with `Database:UseEntraAuthentication` set to `true` the API signs in with a Microsoft Entra access token instead of a password, so the connection string has none.

## Email ingestion

The same storage account also holds two more **private** containers, `email-intake` (attachments until their draft is decided or expires) and `vehicle-documents` (originals users keep with a record). The API creates them at startup; the same managed identity and role as above apply. Defender for Storage is not enabled on the deployed account (cost); it remains a recommendation for malware scanning of uploads. Extraction needs an Azure OpenAI resource: the deployment uses the shared `aif-shared-acc` account and grants the API's identity the *Cognitive Services OpenAI User* role through Bicep (`bicep/modules/ai-access.bicep`); the endpoint and deployment arrive as `EmailIntake:Extraction:Endpoint` and `EmailIntake:Extraction:Deployment`. The Entra side (the `Email.Ingest` app role and granting it to the workflow) is in [entra/README.md](entra/README.md).

### Logic App contract

The Logic App turns each incoming email into one JSON request and calls the API:

- `POST {api}/api/intake/emails` with header `Content-Type: application/json` and authentication type *Managed identity* (audience `api://<api-client-id>`). The identity must hold the `Email.Ingest` app role; a user token or any other role gets `403`. In the deployed environment the address is `https://ca-api-mygarage.<default domain of cae-shared>`, the identity is `id-mygarage-intake`, and the Logic App is created by [logic-app/New-IntakeLogicApp.ps1](logic-app/README.md) (not by the pipeline).
- Body: see [`specs/007-email-ingestion-api/contracts/intake-email.example.json`](../specs/007-email-ingestion-api/contracts/intake-email.example.json). Required: `messageId` (unique per email, up to 512 characters; it is the idempotency key, so sending the same email again is safe), `receivedAt`, `from` (the sender address, up to 320 characters), `subject` (up to 500), and `bodyText` (plain text, up to 100,000 characters; convert HTML to text in the workflow). Optional `attachments`: at most 5 files that are not inline images (inline images such as signature logos are flagged `isInline: true` and ignored), each `fileName`, `contentType`, and `contentBase64`, up to 10 MB each and 15 MB in total after decoding. Only PDF, JPEG, PNG, and WebP files are read; the type is decided from the file bytes, not from the name or `contentType`.
- Answer: `202` with `{ "submissionId": "...", "status": "received" }` for every valid request, whether or not the sender matches a user and whether or not the message id was seen before. The body never reveals whether a user exists.
- Retry policy: retry on `429` (honor `Retry-After`; the endpoint allows 120 requests per minute) and `503` (honor `Retry-After`; storage is temporarily unavailable), with exponential backoff and a small number of attempts. Because the message id makes calls idempotent, retrying the whole message is safe. **Do not retry** `400` (fix the request: it names the invalid field), `401` and `403` (fix the identity or role), or `413` (the message is too large; drop or shrink attachments).
- No polling: the workflow never reads results. Processing happens after the `202`; the owner is notified inside the app and reviews drafts there.
