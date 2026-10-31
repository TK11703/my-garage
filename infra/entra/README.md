# Entra setup

`Initialize-Entra.ps1` registers the My Garage web app and API in a Microsoft Entra ID tenant and stores the resulting
configuration in the AppHost user-secrets, so `aspire run` can start both services with working sign-in.

## What it creates

| Registration | Purpose | Key settings |
|---|---|---|
| `MyGarage API` | Protected backend API | Single tenant, Application ID URI `api://<client-id>`, delegated scope `access_as_user`, access token version 2 |
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
| `-WhatIf` | off | Reports what would change and exits |

## Idempotence

Running the script again finds the registrations by display name and brings them back to the expected state:

- no duplicate registrations, service principals, scopes, or grants are created;
- redirect URIs are added, never removed;
- the web client secret is kept unless none is stored for the current web app or `-RotateSecret` is passed;
- the web app's required permissions are set to the API scope (other permissions already on the app are kept).

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
