# Quickstart: validate the CI/CD pipeline end to end

Prerequisites: PowerShell 7, Azure CLI signed in to the subscription (`az login`), GitHub CLI signed in as a repository administrator (`gh auth login`), the owner account is an Entra admin of `accpsqlshared`. Contracts: [environment-config](contracts/environment-config.md), [azure-setup](contracts/azure-setup.md), [workflows](contracts/workflows.md), [bicep-interface](contracts/bicep-interface.md), [logic-app-intake](contracts/logic-app-intake.md).

## 1. Preview and perform the one-time Azure preparation

```powershell
./infra/azure/Initialize-Azure.ps1 -WhatIf
./infra/azure/Initialize-Azure.ps1
```

Expected: the preview lists what would be created; the real run reports each step as `created`; the secret values are never shown. Run it again: every step reports `unchanged` (SC-007). Confirm nothing shared changed: `az role assignment list --scope <acccrshared id>` shows one additional AcrPush entry, and `az postgres flexible-server db list` shows one additional database `mygarage`.

## 2. Verify a pull request

1. Push a small change on a branch and open a pull request. Expect the check `verify` and its jobs to appear and pass within 15 minutes.
2. Add a compiler warning, push, and expect `verify` to fail naming the warning. Revert it.
3. Break one test and expect the failing test to be listed in the run summary. Revert it.
4. Change an applied migration script and expect the `migrations` job to fail (the database tests still pass because they start from an empty database). Revert it.
5. Push a documentation-only change in another pull request: `verify` still succeeds quickly with the heavy jobs skipped.
6. Push twice quickly to one pull request: the first run shows `cancelled`.

## 3. Protect `main`

```powershell
./infra/github/Initialize-GitHub.ps1 -WhatIf
./infra/github/Initialize-GitHub.ps1
```

Expected: a pull request with a failing or running `verify` cannot be merged; a direct push to `main` is refused.

## 4. First deployment

Merge the pull request. Expected without any manual step except the owner's approval of the pending `production` deployment (the environment has a required reviewer): workflow `deploy` runs `verify`, builds `mygarage/api` and `mygarage/web` with tag `sha-<commit>`, shows a what-if summary, deploys, and the health step passes. Check:

```powershell
az containerapp show -n ca-api-mygarage -g rg-apps --query "properties.configuration.ingress.fqdn" -o tsv
az containerapp revision list -n ca-api-mygarage -g rg-apps -o table
curl -s https://<api address>/health        # Healthy
curl -s https://<web address>/alive         # Healthy
```

Then open the web address, sign in, and add a vehicle (SC-009). Signing in is also the proof that the web app's federated credential works: the registration `MyGarage Azure Web` has no client secret (`az ad app credential list --id <web client id>` returns nothing). The first request after idle takes longer because both apps start from zero.

## 5. Failure behaviour

1. Merge a change that makes `/health` fail (for example a bad configuration in a throwaway branch). Expected: the run fails, `az containerapp revision list` shows the old revision still active with 100% traffic, the app stays reachable (SC-004).
2. Run `deploy` manually with `commit` set to the last good commit: expected to redeploy that tag after verification and return to healthy (SC-006).
3. Run `deploy` with `dryRun` true: expected to stop after what-if with no change.

## 6. Secrets and drift checks

```powershell
git grep -n -i -E "password|secret|client_secret" -- .github infra ':!*.md'   # expect only parameter names, never values
gh run view --log | Select-String -Pattern "<a known secret fragment>"       # expect no match
```

Rename or remove a mapped resource only in a throwaway copy, not production; the deployment is expected to fail naming it and to create no substitute (FR-017).

## 7. Email intake

1. The owner creates the Outlook connection for `mygarage-intake@outlook.com` (portal: Logic Apps connection `outlook`, authorise) in `rg-apps`, region `northcentralus`.
2. Create the Logic App:

   ```powershell
   ./infra/logic-app/New-IntakeLogicApp.ps1 -OutlookConnectionId <connection resource id> -WhatIf
   ./infra/logic-app/New-IntakeLogicApp.ps1 -OutlookConnectionId <connection resource id>
   ```

   Expected: `created`; a second run reports `unchanged`; the managed identity holds the `Email.Ingest` role.
3. As a user already signed in to My Garage, forward a service invoice (PDF) to `mygarage-intake@outlook.com`. Within about 5 minutes the run history shows a `202` from the API, and a draft with a notification appears in the web app. Open the draft, view the PDF in Chrome, accept it (this completes the walkthrough deferred from spec 007, FR-026).
4. Send mail from an address that matches no user: the API still answers `202`, and nothing appears for any user.

## 8. Cost and cleanup checks

- `az containerapp show` for both apps reports `minReplicas: 0`; the web app `maxReplicas: 1`.
- No Defender plan exists on the storage account.
- Removing the app later: delete the two Container Apps, the three identities, the Logic App, the storage account, the database `mygarage` and its owner principal, the pipeline and Azure Entra app registrations, and the role assignments listed in [data-model](data-model.md). This is a manual, deliberate action and is not performed by any script.
