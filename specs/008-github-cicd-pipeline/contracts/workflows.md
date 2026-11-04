# Contract: Workflows

All workflow files are in `.github/workflows/`. Every `uses:` is pinned to a full commit SHA. Default `permissions: contents: read`.

## `verify.yml` (reusable, `workflow_call`)

| Input | Type | Default | Meaning |
|---|---|---|---|
| `ref` | string | the caller's commit | Commit to verify (used by manual redeploys) |

Jobs (runner `ubuntu-latest`, timeout 30 minutes each):

| Job | Runs when | Does | Fails when |
|---|---|---|---|
| `changes` | always | Compares the commit with its base and outputs `code` (anything outside docs and `.specify`/`specs`), `bicep` (`infra/bicep/**`) | never |
| `build-test` | `code == true` | `actions/setup-dotnet` from `global.json`; `dotnet build MyGarage.slnx -c Release` (warnings are errors via `Directory.Build.props`); `dotnet test MyGarage.slnx -c Release --no-build --logger trx` for the four test projects; runs `Write-TestSummary.ps1`; uploads TRX files | build warning or error, any failing test, Docker unavailable |
| `bicep` | `bicep == true` | `az bicep build` and `az bicep lint` on `infra/bicep/main.bicep` (skipped message when the file does not exist yet) | any compile error or lint error |
| `migrations` | pull requests only | Compares the pull request with its merge base and fails when any existing file under `src/MyGarage.Database/Scripts/Migrations/` was modified, renamed, or deleted (new numbered scripts are fine); the database tests only detect a changed checksum in an already-migrated database, so this is what enforces FR-003 for edits | an applied migration script was changed |

The reusable workflow has no gate job of its own: a calling job succeeds only when every called job succeeded or was skipped. The required status check name is **`verify`**, a job in `ci.yml` (see below), because a check reported by a reusable workflow is named `<caller job> / <called job>`.

## `ci.yml`

| Trigger | Behaviour |
|---|---|
| `pull_request` targeting `main` (opened, synchronize, reopened) | job `checks` calls `verify.yml`; job `verify` (`needs: checks`, `if: always()`) succeeds only when `checks` succeeded; no secrets, no `id-token` |
| `push` to `main` | not used here (CD covers it) |

Concurrency group `ci-${{ github.event.pull_request.number }}`, `cancel-in-progress: true`. Fork pull requests run with the read-only token and no secrets.

## `deploy.yml`

| Trigger | Notes |
|---|---|
| `push` to `main` | automatic delivery of the merge commit |
| `workflow_dispatch` | inputs `commit` (default: the selected ref) and `dryRun` (boolean, default false: stop after what-if) |

Concurrency group `deploy-production`, `cancel-in-progress: false`.

| Job | Needs | Permissions | Environment | Steps |
|---|---|---|---|---|
| `verify` | none | `contents: read` | none | `uses: ./.github/workflows/verify.yml` with the commit |
| `build-images` | `verify` | `contents: read`, `id-token: write` | none (federated subject `ref:refs/heads/main`) | `azure/login` (OIDC) -> `az acr login` -> if both tags exist stop, else `dotnet publish -t:PublishContainer` for Api and Web with `ContainerRegistry=acccrshared.azurecr.io`, `ContainerRepository=mygarage/api|web`, `ContainerImageTags=sha-<commit>` |
| `deploy` | `build-images` | `contents: read`, `id-token: write` | `production` | `azure/login` (OIDC) -> `az deployment group what-if` -> (unless `dryRun`) `az deployment group create -g rg-apps --mode Incremental` with `imageTag` -> `Test-Health.ps1` -> job summary |

`Test-Health.ps1` inputs: API address and web address (Bicep outputs), expected tag. It polls `GET {address}/health` until HTTP 200 or a 10-minute timeout, and checks with the Container Apps CLI that the active revision of each app runs image tag `sha-<commit>`. Exit code is non-zero otherwise.

Run summary (written to `$GITHUB_STEP_SUMMARY`): commit, tag, verify result, what-if resource count, deployed addresses, health outcome. No secret values, no email or user data.

## Required repository settings (applied by `Initialize-GitHub.ps1`)

| Setting | Value |
|---|---|
| Ruleset on `main` | pull request required; status check `verify` required; no force push; no deletion |
| Environment `production` | holds the variables and secrets in [environment-config.md](environment-config.md); optional required reviewer |
| Actions permissions | default workflow token read-only |
| Dependabot | `.github/dependabot.yml` for `github-actions` and `nuget`, weekly |
