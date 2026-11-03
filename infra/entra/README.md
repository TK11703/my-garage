# Entra setup

`Initialize-Entra.ps1` registers the My Garage web app and API in a Microsoft Entra ID tenant and stores the resulting
configuration in the AppHost user-secrets, so `aspire run` can start both services with working sign-in.

## What it creates

| Registration | Purpose | Key settings |
|---|---|---|
| `MyGarage API` | Protected backend API | Single tenant, Application ID URI `api://<client-id>`, delegated scope `access_as_user`, app role `Email.Ingest` (for the email-intake workflow), optional access-token claim `email`, access token version 2 |
| `MyGarage Web` | Blazor web app (OpenID Connect, authorization code flow) | Single tenant, redirect URIs `https://localhost:<port>/signin-oidc` and `https://localhost:<port>/signout-callback-oidc`, delegated permission on the API scope, a client secret |

It also creates the service principals, and tries to grant tenant-wide admin consent for the web app's permission on the API.

Values written to the AppHost user-secrets (`src/MyGarage.AppHost`):

| Key | Secret |
|---|---|
| `AzureAd:TenantId` | no |
| `AzureAd:Web:ClientId` | no |
| `AzureAd:Web:ClientSecret` | yes |
| `AzureAd:Api:ClientId` | no |
| `AzureAd:Api:Scope` | no |

The secret is written straight to user-secrets. It is never printed or written to a file in the repository.

## Prerequisites

- PowerShell 7+, .NET SDK 10, [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), Aspire CLI
- An account that can create app registrations in the tenant (Application Administrator or higher)
- Tenant-wide admin consent also requires Cloud Application Administrator, Application Administrator, or Global Administrator.
  Without it the script still succeeds and prints a consent link for an administrator.
- Trusted development certificate: `dotnet dev-certs https --trust`

## Run it

```powershell
az login --tenant <tenant-id> --allow-no-subscriptions
./infra/entra/Initialize-Entra.ps1 -TenantId <tenant-id>
```

Preview without changing anything (read-only queries still run):

```powershell
./infra/entra/Initialize-Entra.ps1 -TenantId <tenant-id> -WhatIf
```

Then start the system from the repository root and browse to `https://localhost:7080`:

```powershell
aspire run
```

## Parameters

| Parameter | Default | Description |
|---|---|---|
| `-TenantId` | tenant of the current `az` session | Must match the tenant you are signed in to |
| `-WebPort` | `7080` | HTTPS port of `MyGarage.Web`. Entra matches redirect URIs exactly, so it must equal `Web:HttpsPort` in the AppHost (default `7080`) |
| `-NamePrefix` | `MyGarage` | Display-name prefix of the two registrations |
| `-AppHostProject` | `src/MyGarage.AppHost` | Project whose user-secrets receive the values |
| `-RotateSecret` | off | Creates a new web client secret even if one is stored |
| `-WebBaseUri` | none | Public address of the deployed web app; redirect URIs become `<uri>/signin-oidc` and `<uri>/signout-callback-oidc` instead of the localhost ones |
| `-GitHubEnvironment` | none | Writes `AZURE_TENANT_ID`, `API_ENTRA_CLIENT_ID`, `API_ENTRA_SCOPE`, and `WEB_ENTRA_CLIENT_ID` as variables of this GitHub environment instead of the AppHost user-secrets (which are then not read or written) |
| `-ManagedIdentityPrincipalId` | none | Object id of a user-assigned managed identity; the web registration gets the federated credential `mygarage-web-managed-identity` for it and no client secret is created |
| `-Repository` | `TK11703/my-garage` | Repository that owns the GitHub environment |
| `-WhatIf` | off | Reports what would change and exits |

## Registrations for the deployed environment

The deployed app uses its own registrations, separate from the local ones. `infra/azure/Initialize-Azure.ps1` runs the script like this:

```powershell
./infra/entra/Initialize-Entra.ps1 -NamePrefix 'MyGarage Azure' `
    -WebBaseUri https://ca-web-mygarage.<default domain of cae-shared> `
    -ManagedIdentityPrincipalId <principal id of id-mygarage-web> `
    -GitHubEnvironment production
```

It creates `MyGarage Azure API` and `MyGarage Azure Web`, adds a federated identity credential (issuer `https://login.microsoftonline.com/<tenant>/v2.0`, subject the managed identity's principal id, audience `api://AzureADTokenExchange`) to the web registration, and stores no client secret anywhere. The local registrations `MyGarage API` and `MyGarage Web` are not touched.

## Email intake workflow (Logic App)

The script creates the `Email.Ingest` app role on `MyGarage API` and adds the `email` optional claim to its access tokens (the API matches an
email's sender to a user by the `email` claim, falling back to `preferred_username`). Users become matchable after their next sign-in.
The script does not create the workflow's identity. To let the Logic App call `POST /api/intake/emails`:

1. Give the Logic App a system-assigned managed identity (or use a dedicated app registration with a certificate or secret kept outside the repository).
2. Assign the role to that identity's service principal:

   ```powershell
   $api = Get-MgServicePrincipal -Filter "appId eq '<api-client-id>'"
   $role = $api.AppRoles | Where-Object Value -eq 'Email.Ingest'
   New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId <workflow-principal-id> `
       -PrincipalId <workflow-principal-id> -ResourceId $api.Id -AppRoleId $role.Id
   ```

3. In the Logic App HTTP action use authentication type *Managed identity*, audience `api://<api-client-id>`, method `POST`, header `Content-Type: application/json`, and the body described in
   `specs/007-email-ingestion-api/contracts/intake-email.example.json`. The endpoint answers `202` for every valid call; retry on `429` and `503`
   (honor `Retry-After`) and do not retry on `400`, `401`, `403`, or `413`.

The API refuses user tokens on the intake endpoint, and refuses the workflow's token on every other endpoint.

### Reading attachments with AI

The API reads PDF and image attachments with an Azure OpenAI deployment that accepts PDF and image input (Responses API, structured output).
Configure `EmailIntake:Extraction:Endpoint` (for example `https://<resource>.openai.azure.com`) and `EmailIntake:Extraction:Deployment` (the deployment name)
in the AppHost user-secrets (they are passed to the API) or as API settings, and give the API's identity (your developer account locally, a managed identity in Azure)
the `Cognitive Services OpenAI User` role on the resource. No key is used. Without these settings extraction is disabled and attachments reach the user as
manual-entry drafts. The identity also needs `Storage Blob Data Contributor` on the storage account that holds the `email-intake` and `vehicle-documents` containers;
turn on Microsoft Defender for Storage on that account to scan uploads for malware.

## Idempotence

Running the script again finds the registrations by display name and brings them back to the expected state:

- no duplicate registrations, service principals, scopes, or grants are created;
- redirect URIs are added, never removed;
- the web client secret is kept unless none is stored for the current web app or `-RotateSecret` is passed;
- the web app's required permissions are set to the API scope (other permissions already on the app are kept);
- the `Email.Ingest` role keeps its id and the `email` claim is added once (other roles and claims already on the API app are kept).

If more than one registration has the same display name the script stops and asks you to remove the duplicates.

## Changing the web port

1. Pick the port and run the script with `-WebPort <port>` (this adds the new redirect URIs).
2. Tell the AppHost the same port: `dotnet user-secrets set Web:HttpsPort <port> --project src/MyGarage.AppHost`.

## Rotating the secret

```powershell
./infra/entra/Initialize-Entra.ps1 -TenantId <tenant-id> -RotateSecret
```

Secrets are valid for 180 days. Remove old secrets from the app registration in the Entra portal.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `You are not signed in to the Azure CLI` | Run `az login --tenant <tenant-id> --allow-no-subscriptions` |
| `The Azure CLI is signed in to tenant ... not ...` | Sign in to the tenant you pass with `-TenantId` |
| `Authorization_RequestDenied` or `Insufficient privileges` | Your account cannot create app registrations in this tenant |
| Warning that admin consent could not be granted | Ask an administrator to open the printed `adminconsent` link, or to grant consent in the portal under *MyGarage Web > API permissions* |
| `AADSTS50011` redirect URI mismatch at sign-in | The port in the browser differs from `-WebPort`; run the script with the right port |
| `AADSTS7000215` invalid client secret | The stored secret expired or was removed; rerun with `-RotateSecret` |
| AppHost: `Missing configuration 'AzureAd:...'` | The script has not been run for this machine, or ran against a different AppHost project |

## Removing the registrations

Delete `MyGarage Web` and `MyGarage API` under *Entra ID > App registrations* in the portal, and clear the AppHost user-secrets
(`dotnet user-secrets clear --project src/MyGarage.AppHost`).
