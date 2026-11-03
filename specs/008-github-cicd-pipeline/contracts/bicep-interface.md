# Contract: Bicep interface

Entry point `infra/bicep/main.bicep`, `targetScope = 'resourceGroup'`, deployed to `rg-apps` (region `northcentralus`) with `az deployment group create --mode Incremental`. Modules that create resources in `rg-platform` are deployed with `scope: resourceGroup(platformResourceGroup)`.

## Parameters

Listed in [environment-config.md](environment-config.md#bicep-parameters-from-the-environment-mainbicepparam). There are no secure parameters. `imageTag` is validated (`@minLength`/`@maxLength` 44) and must start with `sha-`.

## Resources created or updated (managed)

| Module | Scope | Resource | Notes |
|---|---|---|---|
| `identities` | `rg-apps` | User-assigned identities `id-mygarage-api`, `id-mygarage-web` | also deployed once by `Initialize-Azure.ps1`; `id-mygarage-intake` is not here (created by the Logic App script) |
| `storage` | `rg-platform` | Storage account `stmygarage<uniqueString>`, StorageV2, Standard_LRS, TLS 1.2, HTTPS only, shared key disabled, anonymous blob access disabled, default OAuth authentication | default hot access tier is fine at this size; no Defender plan |
| `storage` | `rg-platform` | Role assignment *Storage Blob Data Contributor* on the account for `id-mygarage-api` | deterministic guid name so reruns do not duplicate |
| `platform-database` | `rg-platform` | Database `mygarage` on `accpsqlshared`, UTF8 | the server is `existing`; the owner and Entra principal are set by `Initialize-Azure.ps1`, not here |
| `ai-access` | `rg-platform` | Role assignment *Cognitive Services OpenAI User* on `aif-shared-acc` for `id-mygarage-api` | the account is `existing` |
| `container-apps` | `rg-apps` | Container App `ca-api-mygarage` | image `acccrshared.azurecr.io/mygarage/api:<imageTag>`, identities `id-mygarage-api` + `id-shared-acrpull`, registry authentication by `id-shared-acrpull`, 0.25 vCPU / 0.5 GiB, replicas 0..2, HTTP scale rule 20 concurrent requests, cooldown 300 s, plain setting `ConnectionStrings__garagedb` (no password, so no secret) |
| `container-apps` | `rg-apps` | Container App `ca-web-mygarage` | image `.../mygarage/web:<imageTag>`, identities `id-mygarage-web` + `id-shared-acrpull`, replicas 0..1, setting `AzureAd__Web__ManagedIdentityClientId` (no secret), depends on the API app |

## Existing resources (referenced only, never declared as managed)

`cae-shared`, `acccrshared`, `id-shared-acrpull`, `accpsqlshared`, `aif-shared-acc`. A missing or inaccessible one fails the deployment with its name (FR-017); no substitute is created.

## Outputs

| Output | Use |
|---|---|
| `apiAddress` | `https://<fqdn>` of the API; health check and Logic App script |
| `webAddress` | `https://<fqdn>` of the web app; health check, Entra redirect URIs, summary |
| `storageServiceUri` | informational |
| `apiRevisionName`, `webRevisionName` | health check compares the active revision |

## Properties the template guarantees

- Incremental mode only; no resource it does not declare is touched.
- No secret value appears in outputs, tags, or the what-if text (secure parameters are masked).
- Re-running with the same inputs yields no change.
- Lint is clean at error level (`bicepconfig.json`).
