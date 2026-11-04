# GitHub repository setup

`Initialize-GitHub.ps1` applies the repository settings the CI/CD pipeline relies on. It is run by a repository administrator, never by the pipeline.

## What it sets

| Setting | Value |
|---|---|
| Ruleset `protect-main` | On the default branch: a pull request is required, the status check `verify` must pass, force pushes and deletion are blocked. No approving review is required (a single maintainer cannot approve their own pull request). |
| Default workflow token | Read-only; workflows cannot approve pull requests |
| Environment `production` reviewer (optional) | `-RequiredReviewer <login>` adds the user as a required reviewer. Self-review stays allowed so the owner can approve their own deployment |
| Environment `production` branches (optional) | `-RestrictEnvironmentToMain` limits deployments to branch `main` |

Each setting is compared with its current state and reported as `created`, `updated`, or `unchanged`. Re-running changes nothing that is already correct.

## Run it

```powershell
gh auth login
./infra/github/Initialize-GitHub.ps1 -WhatIf
./infra/github/Initialize-GitHub.ps1
```

Run it after the first pipeline run has reported the check `verify`: GitHub can only require a check it has already seen. Turn on `-RestrictEnvironmentToMain` once the deployment rehearsals from throwaway branches are finished.

The environment `production` itself, its variables, and the Azure trust are created by [`infra/azure/Initialize-Azure.ps1`](../azure/README.md).

## Parameters

| Parameter | Default | Description |
|---|---|---|
| `-Repository` | `TK11703/my-garage` | Repository in `owner/name` form |
| `-RequiredReviewer` | none | GitHub login added as required reviewer on `production` |
| `-RestrictEnvironmentToMain` | off | Only `main` may deploy to `production` |
| `-WhatIf` | off | Reports what would change |
