# Azure preparation

`Initialize-Azure.ps1` prepares Azure and GitHub once so the pipeline in `.github/workflows/` can deploy My Garage to the existing shared resources. It is run by the subscription owner, never by the pipeline, and is safe to run again: every step compares the current state and reports `created`, `updated`, or `unchanged`. It never deletes or replaces anything, creates no secret, and prints none.

## Prerequisites

- `az login` as an Owner of the subscription who can create app registrations in the tenant
- The signed-in account is a Microsoft Entra administrator of the shared PostgreSQL server `accpsqlshared`, and its current IP address is allowed by the server firewall (or pass `-TemporaryFirewallRule`)
- Azure CLI, GitHub CLI (`gh auth login` as a repository administrator), PowerShell 7, and a build of the repository (`dotnet build tests/MyGarage.Database.Tests`): the database steps run SQL through the Npgsql library from that build because the `az` rdbms-connect extension is unreliable on some machines

## Run it

```powershell
./infra/azure/Initialize-Azure.ps1 -WhatIf
./infra/azure/Initialize-Azure.ps1
```

## Steps

| # | Step | Result when repeated |
|---|---|---|
| 1 | Create GitHub environment `production` when missing (its protection rules are never changed here) | unchanged |
| 2 | App registration and service principal `mygarage-github-oidc` with federated credentials `github-environment-production` and `github-main-branch` (subjects built from the repository's OIDC subject prefix, read from GitHub) | unchanged |
| 3 | Roles for that identity: Contributor and User Access Administrator on the subscription (temporary, see below) and AcrPush on `acccrshared` | unchanged |
| 4 | User-assigned identities `id-mygarage-api` and `id-mygarage-web` in `rg-apps` from `infra/bicep/modules/identities.bicep` | unchanged |
| 5 | Entra registrations `MyGarage Azure API` and `MyGarage Azure Web` through [`infra/entra/Initialize-Entra.ps1`](../entra/README.md), with the web registration's federated credential for `id-mygarage-web` and no client secret | unchanged |
| 6 | Database `mygarage` on `accpsqlshared` from `infra/bicep/modules/platform-database.bicep` | unchanged |
| 7 | PostgreSQL Entra principal `id-mygarage-api`, owner of `mygarage`; `PUBLIC` can no longer connect to it | unchanged |
| 8 | Repository variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (repository scope because the `build-images` job has no environment and still signs in) and environment variables `EXTRACTION_ENDPOINT`, `EXTRACTION_DEPLOYMENT` (the Entra variables come from step 5) | unchanged |

## Parameters

| Parameter | Default | Description |
|---|---|---|
| `-SubscriptionId` | current `az` subscription | Safety check: a mismatch stops the script |
| `-ResourceGroup` | `rg-apps` | Resource group of the apps and identities |
| `-PlatformResourceGroup` | `rg-platform` | Resource group of the shared server, registry, AI account, and storage |
| `-Location` | `northcentralus` | Azure region |
| `-Repository` | `TK11703/my-garage` | Repository that deploys |
| `-TemporaryFirewallRule` | off | Adds a PostgreSQL firewall rule for this machine's public IP for the database step (7) and removes it afterwards; use it when the server does not already allow this machine |
| `-WhatIf` | off | Reports what would change |

## Temporary rights

The pipeline identity holds Contributor and User Access Administrator on the whole subscription while the pipeline is brought up (owner decision, spec FR-019 exception). Narrowing it is a follow-up, listed in [`infra/README.md`](../README.md#follow-ups): Contributor on `rg-apps` and `rg-platform` resources only, a custom role for joining `cae-shared` and creating one database on `accpsqlshared`, and User Access Administrator limited to `rg-apps`, the storage account, and `aif-shared-acc`. Its trust is limited to this repository's `production` environment and `main` branch.

## Drift and recovery

If a shared resource is renamed or removed, or an assignment is lost, the deployment fails naming the resource and never creates a substitute. Run the script again to restore the objects it owns.

Removing the app is manual and deliberate: delete the two Container Apps, the identities, the Logic App, the storage account, the database `mygarage` and its owner principal, the registrations, and the role assignments. No script does this.
