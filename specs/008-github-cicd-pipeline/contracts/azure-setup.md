# Contract: One-time setup scripts

All scripts are PowerShell 7, support `-WhatIf`, are idempotent, and never print secret values. The deployed environment needs no secret, so these scripts create and store none. They are run by the owner, not by the pipeline.

## `infra/azure/Initialize-Azure.ps1`

Prerequisites: `az login` as an account that is Owner of the subscription, with Entra rights to create app registrations, an Entra admin of `accpsqlshared` (the owner is one), the owner's current IP allowed by the server firewall (or `-TemporaryFirewallRule`, which adds a rule for this machine for the database step and removes it afterwards), PowerShell 7, Azure CLI, a build of the repository (the database steps run SQL through Npgsql from it because the `rdbms-connect` extension is unreliable on some machines), GitHub CLI authenticated as a repository administrator.

| Parameter | Default | Meaning |
|---|---|---|
| `-SubscriptionId` | current `az` subscription | Safety check against the wrong subscription |
| `-ResourceGroup` | `rg-apps` | Resource group of the apps and identities |
| `-PlatformResourceGroup` | `rg-platform` | Resource group of the shared server, registry, AI account, and storage |
| `-Location` | `northcentralus` | |
| `-Repository` | `TK11703/my-garage` | For federated credential subjects and GitHub settings |
| `-WhatIf` | off | |

Steps and outcomes:

| # | Step | Result when repeated |
|---|---|---|
| 1 | Create GitHub environment `production` when missing | unchanged |
| 2 | Create app registration and service principal `mygarage-github-oidc` with federated credentials `github-environment-production` and `github-main-branch` (subjects built from the repository's OIDC subject prefix read from GitHub) | unchanged |
| 3 | Assign it **Contributor** and **User Access Administrator** at subscription scope (temporary, see research R5) and **AcrPush** on `acccrshared` | unchanged |
| 4 | Deploy `infra/bicep/modules/identities.bicep` (creates `id-mygarage-api` and `id-mygarage-web` in `rg-apps`) | no change |
| 5 | Run `infra/entra/Initialize-Entra.ps1 -NamePrefix 'MyGarage Azure' -WebBaseUri <web address> -ManagedIdentityPrincipalId <principal id of id-mygarage-web> -GitHubEnvironment production`, creating `MyGarage Azure API` and `MyGarage Azure Web` (with its federated credential and no secret) and sending their ids to the environment | unchanged |
| 6 | Deploy `infra/bicep/modules/platform-database.bicep` (creates database `mygarage` only) | no change |
| 7 | As the owner (Entra token), create the PostgreSQL Entra principal `id-mygarage-api`, make it owner of `mygarage`, revoke public connect | unchanged |
| 8 | Set the remaining environment variables (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `EXTRACTION_ENDPOINT`, `EXTRACTION_DEPLOYMENT`) from [environment-config.md](environment-config.md) | unchanged |

Nothing is ever deleted or replaced, and nothing shared (`cae-shared`, `accpsqlshared`, `acccrshared`, `aif-shared-acc`) is reconfigured beyond the role assignments and database objects listed above.

## `infra/entra/Initialize-Entra.ps1` (extended)

New optional parameters; existing behaviour and defaults are unchanged.

| Parameter | Default | Meaning |
|---|---|---|
| `-WebBaseUri` | none | When given, the web registration's redirect URIs are `<uri>/signin-oidc` and `<uri>/signout-callback-oidc` instead of the localhost ones |
| `-GitHubEnvironment` | none | When given, the client ids, scope, and tenant go to this GitHub environment (variables) and the AppHost user-secrets are not read or written |
| `-ManagedIdentityPrincipalId` | none | When given, the web registration gets a federated credential for that managed identity (issuer `https://login.microsoftonline.com/<tenant>/v2.0`, audience `api://AzureADTokenExchange`) and **no client secret** is created or stored |
| `-Repository` | `TK11703/my-garage` | Used with `-GitHubEnvironment` |

## `infra/github/Initialize-GitHub.ps1`

Prerequisites: GitHub CLI authenticated as a repository administrator.

| Parameter | Default | Meaning |
|---|---|---|
| `-Repository` | `TK11703/my-garage` | |
| `-RequiredReviewer` | none | GitHub user to require approval on environment `production` |
| `-WhatIf` | off | |

Creates or updates: the optional required reviewer on environment `production`; the ruleset on `main` (pull request required, status check `verify`, no force push, no deletion); default workflow token permission read-only. Re-run reports `unchanged` for anything already correct. Run it after the first pipeline run has reported `verify`.

## `infra/logic-app/New-IntakeLogicApp.ps1`

See [logic-app-intake.md](logic-app-intake.md). It creates `id-mygarage-intake`, the Logic App, and the `Email.Ingest` grant; it is never run by the pipeline.

## Drift handling (FR-017)

If a mapped resource is renamed, removed, or loses an assignment, the scripts report it and the deployment fails naming the resource. Re-running `Initialize-Azure.ps1` restores the assignments and objects it owns; it never creates substitutes for shared resources.
