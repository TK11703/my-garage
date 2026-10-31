# Contract: Entra Setup Script

`infra/entra/Initialize-Entra.ps1` (PowerShell 7+, Azure CLI).

## Parameters

| Parameter | Default | Description |
|---|---|---|
| `-TenantId` | current `az` context tenant | Tenant to register apps in. |
| `-WebPort` | `7080` | HTTPS port of `MyGarage.Web`; must match the AppHost. |
| `-NamePrefix` | `MyGarage` | Display name prefix: `<prefix> Web`, `<prefix> API`. |
| `-AppHostProject` | `src/MyGarage.AppHost` | Project whose user-secrets receive the output. |
| `-RotateSecret` | off | Create a new web client secret even if one was already stored. |
| `-WhatIf` | off | Show intended changes without applying them. |

## Preconditions

- `az login` completed with rights to create app registrations in the tenant.
- The AppHost project exists and has a `UserSecretsId`.

## Behaviour (idempotent)

1. Find or create the API app (single tenant, `api://<clientId>` identifier URI, scope `access_as_user`, token version 2) and its service principal.
2. Find or create the Web app (single tenant, redirect URIs `https://localhost:<WebPort>/signin-oidc` and `https://localhost:<WebPort>/signout-callback-oidc` (the latter doubles as the post-logout redirect URI)), its service principal, and the delegated permission on the API scope; attempt admin consent and report if it requires an administrator.
3. Create a client secret only if none is stored in user-secrets (or `-RotateSecret`).
4. Store in AppHost user-secrets: `AzureAd:TenantId`, `AzureAd:Web:ClientId`, `AzureAd:Web:ClientSecret`, `AzureAd:Api:ClientId`, `AzureAd:Api:Scope`.
5. Print a summary of IDs (never the secret) and next steps.

## Guarantees

- Re-running produces no duplicate registrations and leaves existing values unchanged except as required (redirect URIs, scope, permissions).
- The secret is never written to disk outside user-secrets, to the console, or to logs.
- Non-zero exit code and a clear message on missing prerequisites or insufficient permissions.
