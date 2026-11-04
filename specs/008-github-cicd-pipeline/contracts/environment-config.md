# Contract: Environment configuration

## GitHub environment `production`

| Name | Kind | Source | Used for |
|---|---|---|---|
| `AZURE_CLIENT_ID` | **repository** variable | `Initialize-Azure.ps1` (`mygarage-github-oidc`) | `azure/login` in `build-images` (no environment) and `deploy` |
| `AZURE_TENANT_ID` | **repository** variable (also written to the environment by `Initialize-Entra.ps1`) | script | `azure/login`, app setting `AzureAd__TenantId` |
| `AZURE_SUBSCRIPTION_ID` | **repository** variable | script | `azure/login`, deployment |
| `API_ENTRA_CLIENT_ID` | variable | `MyGarage Azure API` registration | `AzureAd__Api__ClientId` (both apps) |
| `API_ENTRA_SCOPE` | variable | `access_as_user` | `AzureAd__Api__Scope` (both apps) |
| `WEB_ENTRA_CLIENT_ID` | variable | `MyGarage Azure Web` registration | `AzureAd__Web__ClientId` |
| `EXTRACTION_ENDPOINT` | variable | `https://aif-shared-acc.openai.azure.com` | `EmailIntake__Extraction__Endpoint` |
| `EXTRACTION_DEPLOYMENT` | variable | `gpt-4.1-mini` | `EmailIntake__Extraction__Deployment` |

There are no secrets in the GitHub environment: no database password and no web client secret. Nothing in the repository contains a secret value, and values are never echoed by a script or workflow step.

## Bicep parameters from the environment (`main.bicepparam`)

| Parameter | Environment variable / literal | Notes |
|---|---|---|
| `imageTag` | `IMAGE_TAG` | `sha-<40 hex>`; set by the workflow |
| `tenantId`, `apiClientId`, `apiScope`, `webClientId` | variables above | |
| `extractionEndpoint`, `extractionDeployment` | variables above | |
| `location` | literal `northcentralus` | |
| `environmentName`, `environmentResourceGroup` | literals `cae-shared`, `rg-apps` | existing resource |
| `platformResourceGroup` | literal `rg-platform` | storage, database, AI account, registry live here |
| `registryServer`, `pullIdentityName` | literals `acccrshared.azurecr.io`, `id-shared-acrpull` | existing resources |
| `postgresServerName` | literal `accpsqlshared` | existing resource |
| `aiAccountName` | literal `aif-shared-acc` | existing resource |

## Settings the apps receive

### `ca-api-mygarage` (identities `id-mygarage-api`, `id-shared-acrpull`)

| Setting | Value |
|---|---|
| `AZURE_CLIENT_ID` | client id of `id-mygarage-api` |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` |
| `AzureAd__TenantId`, `AzureAd__Api__ClientId`, `AzureAd__Api__Scope` | from variables |
| `ConnectionStrings__garagedb` | `Host=<server fqdn>;Database=mygarage;Username=id-mygarage-api;SSL Mode=Require;Maximum Pool Size=10`, a plain setting because it holds no password |
| `Database__UseEntraAuthentication` | `true` |
| `ConnectionStrings__blobs` | `https://<storage account>.blob.core.windows.net/` |
| `EmailIntake__Extraction__Endpoint`, `EmailIntake__Extraction__Deployment` | from variables |
| `ReleaseId` | commit SHA (informational) |

Probes: startup `GET /alive` (period 5 s, failure threshold 60), liveness `GET /alive` (30 s), readiness `GET /health` (10 s). Port 8080, external ingress, HTTPS only.

### `ca-web-mygarage` (identities `id-mygarage-web`, `id-shared-acrpull`)

| Setting | Value |
|---|---|
| `AZURE_CLIENT_ID` | client id of `id-mygarage-web` |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` |
| `AzureAd__TenantId`, `AzureAd__Web__ClientId`, `AzureAd__Api__ClientId`, `AzureAd__Api__Scope` | from variables |
| `AzureAd__Web__ManagedIdentityClientId` | client id of `id-mygarage-web`; makes the web app prove its identity to Entra with a federated credential instead of a client secret |
| `services__api__https__0` | `https://<API address>` (Bicep output of the API app) |
| `ReleaseId` | commit SHA |

Same probes and ingress; maximum replicas 1.

## Entra redirect URIs on `MyGarage Azure Web`

- `https://ca-web-mygarage.<default domain of cae-shared>/signin-oidc`
- `https://ca-web-mygarage.<default domain of cae-shared>/signout-callback-oidc`

The local registration `MyGarage Web` keeps its localhost URIs and its client secret, and is not touched.

## Federated credential on `MyGarage Azure Web`

| Attribute | Value |
|---|---|
| Name | `mygarage-web-managed-identity` |
| Issuer | `https://login.microsoftonline.com/<tenant id>/v2.0` |
| Subject | object (principal) id of `id-mygarage-web` |
| Audience | `api://AzureADTokenExchange` |

The registration has no client secret. The web app asks its managed identity for an assertion and presents it to Entra as the client credential, both for sign-in and for the on-behalf token requests to the API.
