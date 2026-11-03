# Feature Specification: GitHub CI/CD Pipeline

**Feature Branch**: `008-github-cicd-pipeline`

**Created**: 2026-11-03

**Status**: Draft

**Input**: User description: "we are going to setup a ci/cd pipeline for this application in GitHub. There are already resources in my Azure subscription I would like to reuse."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Verify every change before it can merge (Priority: P1)

A developer opens a pull request against `main`. GitHub automatically builds the whole solution, runs every automated test, and proves that the database migrations apply cleanly to an empty database. The result is reported on the pull request as clear pass/fail checks. A pull request whose checks fail cannot be merged, so the merge gates the project already promises (zero-warning build, all tests passing, clean migrations) are enforced by the repository instead of relying on the reviewer's memory.

**Why this priority**: This is the continuous-integration half of the pipeline. It needs no Azure resources, protects `main` immediately, and is the smallest slice that delivers value on its own.

**Independent Test**: Open a pull request with a healthy change and confirm all checks pass; open one that introduces a compiler warning, one that breaks a test, and one that changes an already-applied migration, and confirm each is reported as failed and cannot be merged.

**Acceptance Scenarios**:

1. **Given** a pull request targeting `main`, **When** it is opened or updated, **Then** the build, the full test suite, and the empty-database migration check run automatically and each reports its result on the pull request.
2. **Given** a change that produces any compiler warning, **When** the checks run, **Then** the build check fails and names the warning.
3. **Given** a change that makes a test fail, **When** the checks run, **Then** the test check fails and the failing tests are identifiable from the pull request without downloading anything.
4. **Given** a change that edits an applied migration or leaves the database unable to migrate from empty, **When** the checks run, **Then** the migration check fails.
5. **Given** one or more required checks have failed or are still running, **When** someone tries to merge the pull request, **Then** the merge is blocked.
6. **Given** a new commit is pushed to a pull request while checks are running, **When** the checks restart, **Then** the superseded run is cancelled so only the latest commit is verified.

---

### User Story 2 - Deploy `main` automatically to the existing Azure environment (Priority: P1)

When a pull request is merged into `main`, GitHub builds the deployable output once and releases it to the Azure resources that already exist in the subscription: the web application, the API, and the supporting database and storage. GitHub proves its identity to Azure without any long-lived password or key stored in the repository. The deployment applies pending database migrations, confirms the new version is healthy, and reports the outcome. If the new version is not healthy, the previously released version stays in service and the failure is reported.

**Why this priority**: Delivers the continuous-delivery half and the stated goal of reusing resources the owner already pays for. It depends on Story 1's verified build being the thing that is released.

**Independent Test**: Merge a small visible change to `main` and confirm that, without any manual step, the running Azure environment serves the new version and reports healthy. Then merge a deliberately broken change and confirm the previous version stays live and the failure is reported.

**Acceptance Scenarios**:

1. **Given** a change has been merged into `main` and its verification passed, **When** the deployment runs, **Then** the web application and API running in Azure are updated to that commit and report healthy.
2. **Given** a deployment is running, **When** it needs to act on the Azure subscription, **Then** it authenticates using a short-lived credential issued to the GitHub repository's workflow; no password, key, or secret that grants Azure access is stored in the repository or its settings.
3. **Given** the new version includes pending database migrations, **When** it is released, **Then** the migrations are applied before the new version takes traffic and each is recorded in the migration ledger.
4. **Given** the new version fails its health check after release, **When** the deployment finishes, **Then** the deployment is reported as failed, the previously released version remains in service, and the failure reason is visible in the run.
5. **Given** verification of the merge commit fails, **When** the deployment would start, **Then** nothing is released.
6. **Given** two merges arrive in quick succession, **When** deployments are triggered, **Then** they run one at a time in merge order and the environment ends on the latest commit.

---

### User Story 3 - Discover and reuse the existing Azure resources safely (Priority: P2)

The owner already has Azure resources (for example a resource group, hosting, database, storage, and monitoring) that should be reused rather than recreated. The pipeline must be wired to exactly those resources. The owner can see which existing resources the pipeline uses, what the pipeline is allowed to do to them, and what still has to be provided because it does not exist yet. Setting this up never deletes, replaces, or silently reconfigures an existing resource, and the setup can be repeated safely.

**Why this priority**: Deployment cannot be configured without knowing what is there, and a careless setup could damage live resources. It is ordered after the first two stories only because it is the preparation that makes Story 2 possible; the inventory itself is a prerequisite step of delivering Story 2.

**Independent Test**: Run the setup against the subscription and confirm it produces an inventory that maps each application component to an existing resource, lists anything missing, changes no existing resource, and can be run a second time with no differences.

**Acceptance Scenarios**:

1. **Given** the Azure subscription contains the owner's existing resources, **When** the setup is performed, **Then** each application component (web, API, database, file storage, extraction service, monitoring) is mapped to an existing resource or flagged as missing, and the mapping is documented.
2. **Given** a required resource does not exist, **When** the setup is performed, **Then** the gap is reported clearly with what is needed, and no unrelated resource is touched.
3. **Given** the setup has already been performed, **When** it is performed again, **Then** it produces no changes.
4. **Given** the pipeline's Azure identity, **When** its permissions are reviewed, **Then** it can act only on the resources the application uses and holds no broader rights than deployment needs (initial exception: see FR-019).

---

### User Story 4 - Keep configuration and secrets out of the repository (Priority: P2)

Environment-specific values (resource names, endpoints, tenant and client identifiers) are supplied to the pipeline through protected GitHub settings, never committed. The deployed application receives its Entra sign-in settings and its storage, database, and extraction settings without a developer hand-editing the running environment. Pipeline logs never print secret values.

**Why this priority**: The constitution forbids committed secrets and the application will not sign users in without correct configuration, so a deployment without this is unusable, but it can be built after the basic flow is shown working.

**Independent Test**: Search the repository and a completed run's logs for secret values and find none; deploy to the environment and confirm users can sign in and the API can reach its database, storage, and extraction service.

**Acceptance Scenarios**:

1. **Given** the pipeline repository, **When** its files and run logs are inspected, **Then** no secret, password, key, or connection string with credentials appears.
2. **Given** a completed deployment, **When** a user browses to the web application and signs in, **Then** sign-in succeeds and the API calls made on their behalf succeed.
3. **Given** a setting changes (for example a new endpoint), **When** it is updated in the protected configuration location and the pipeline runs again, **Then** the deployed application uses the new value without any change to the repository.

---

### User Story 5 - Control and observe releases (Priority: P3)

The owner can start a deployment manually (for example to redeploy a known commit or recover from a transient failure), can see which commit is currently deployed, and can roll back by redeploying an earlier commit. Each run leaves a readable summary of what was verified and deployed, and a failed run notifies the owner.

**Why this priority**: Valuable operational comfort, but the pipeline is useful without it.

**Independent Test**: Trigger a manual deployment of an earlier commit and confirm the environment returns to that commit, the summary names it, and a deliberately failed run produces a notification.

**Acceptance Scenarios**:

1. **Given** the owner selects a previous commit, **When** they start a manual deployment, **Then** the environment is updated to that commit after the same verification and health check.
2. **Given** any completed run, **When** the owner opens it, **Then** a summary shows the commit, what was verified, what was deployed, and the resulting address.
3. **Given** a verification or deployment run fails, **When** it finishes, **Then** the owner is notified through GitHub's standard notification channels.

---

### Edge Cases

- A pull request comes from a fork or an untrusted contributor: the verification must still run safely, and no deployment credentials or secrets are exposed to it.
- Automated tests need a container runtime: the verification environment must provide it, and a missing runtime must fail visibly rather than skip the tests silently.
- A flaky external dependency (package feed, registry) causes a transient failure: a re-run of the failed run must be possible without pushing a new commit.
- A migration partly fails during deployment: the release must stop, the previously released application version must remain in service, and the failure must name the migration. Because migrations are forward-only, a repaired fix ships as a new migration.
- The existing Azure resources have drifted from the documented mapping (renamed, deleted, or permissions changed): the deployment fails with a message naming the missing or inaccessible resource instead of creating a substitute.
- The Azure identity's trust is configured for one repository and branch: a workflow run from any other repository or branch must be unable to deploy.
- Only documentation or non-deployable files changed: the pipeline need not release a new version, but required checks must still be satisfiable so such pull requests can merge.
- The deployed environment shares resources with other workloads in the subscription: the pipeline must not touch anything it was not mapped to.

## Requirements *(mandatory)*

### Functional Requirements

**Continuous integration**

- **FR-001**: The repository MUST automatically verify every pull request targeting `main`, and every push to `main`, by building the full solution with warnings treated as errors.
- **FR-002**: Verification MUST run the complete automated test suite, including tests that need a real database instance, and MUST fail if the container runtime those tests need is unavailable.
- **FR-003**: Verification MUST prove that all migrations apply cleanly to an empty database and MUST fail if an already-applied migration has been altered.
- **FR-004**: Verification results MUST appear as named checks on the pull request, with test results readable from the pull request and build/test outputs retained for later inspection.
- **FR-005**: The pipeline MUST cancel a verification run that has been superseded by a newer commit on the same pull request.
- **FR-006**: `main` MUST be protected so that a pull request cannot be merged until the required verification checks pass and the change arrives through a pull request, consistent with the constitution's merge gates.

**Continuous delivery**

- **FR-007**: A successful verification of a commit on `main` MUST automatically trigger a deployment of that same commit to the existing Azure environment, and a deployment MUST NOT start for a commit whose verification failed.
- **FR-008**: The deployable output MUST be built once per commit and the exact verified output MUST be what is released; it MUST NOT be rebuilt differently for deployment.
- **FR-009**: A deployment MUST update both the web application and the API, and MUST apply any pending database migrations before the new version serves traffic.
- **FR-010**: After release, the deployment MUST check the health of both the web application and the API and MUST report failure, leaving the previously released version in service, if either is unhealthy.
- **FR-011**: Deployments to the environment MUST run one at a time, in order, with the environment ending on the most recently merged commit.
- **FR-012**: The owner MUST be able to start a deployment manually for a chosen commit, which MUST pass the same verification and health checks, enabling roll-back by redeploying an earlier commit.
- **FR-013**: The release MUST be gated by a GitHub deployment environment, so the owner can optionally require an approval before the release proceeds and can see the deployment history in GitHub.

**Reuse of existing Azure resources**

- **FR-014**: The pipeline MUST deploy to Azure resources that already exist in the owner's subscription, and MUST NOT create, replace, rename, or delete them as part of ordinary deployment.
- **FR-015**: The feature MUST document an inventory mapping each application component (web application hosting, API hosting, relational database, private file storage, document-extraction service, monitoring) to the existing Azure resource it will use, and MUST list any component for which no suitable existing resource was found, with what is needed.
- **FR-016**: Any one-time preparation of existing resources (for example granting the pipeline or the application's identity access, or enabling a missing setting) MUST be repeatable without causing changes on repeat, MUST be documented, and MUST NOT remove or weaken existing settings, data, or access.
- **FR-017**: A deployment that cannot find or access a mapped resource MUST fail and name the resource; it MUST NOT provision a substitute.

**Identity and secrets**

- **FR-018**: GitHub MUST authenticate to Azure with short-lived credentials issued to this repository's workflow, restricted to the intended repository and branch/environment; no long-lived Azure password, key, or client secret MUST be stored in the repository or GitHub settings.
- **FR-019**: The pipeline's Azure identity MUST hold only the permissions needed to deploy to the mapped resources, scoped to those resources rather than the whole subscription. *Initial exception (owner decision, 2026-11-03)*: while the pipeline is brought up, its identity temporarily holds Contributor and User Access Administrator at subscription scope to rule out permission problems; narrowing it to the mapped resources is a tracked follow-up and does not change the no-stored-secret and repository/branch/environment trust restrictions of FR-018.
- **FR-020**: Environment-specific, non-secret values MUST be held as protected GitHub environment variables, and any secret the application needs at runtime MUST be held in a protected store (GitHub environment secrets or Azure's secret storage), never in source control.
- **FR-021**: Pipeline logs, summaries, and uploaded outputs MUST NOT contain secret values or sensitive user, vehicle, or service-record data.
- **FR-022**: Workflows run for pull requests from forks or other untrusted sources MUST have no access to deployment credentials or secrets.
- **FR-023**: The deployed application MUST be configured so that user sign-in through Microsoft Entra ID works against the deployed addresses (including the sign-in redirect addresses registered for the web application) and so the API can reach its database, private storage, and extraction service using managed identities where available.

**Operability and documentation**

- **FR-024**: Each run MUST produce a summary identifying the commit, what was verified, what was deployed, and the deployed address, and failed runs MUST notify the owner through GitHub's standard channels.
- **FR-025**: The repository MUST document, in the existing infrastructure documentation, how the pipeline is set up, how the existing resources are mapped, how to perform the one-time preparation, how to run and roll back a deployment, and how to rotate credentials.
- **FR-026**: Once a deployed environment exists, the manual end-to-end walkthrough of the email-intake feature that was deferred to this spec (including the PDF display check in Chrome) MUST be executable against it.
- **FR-027**: Third-party actions used by the pipeline MUST be pinned to a specific immutable version so a changed or compromised upstream action cannot silently alter what runs.
- **FR-028**: Changes that affect only documentation MUST still allow required checks to be satisfied so such pull requests can merge without a full deployment.

### Key Entities

- **Verification run**: One automated build/test/migration check of a specific commit, with a pass/fail outcome per check, retained test results, and a link back to the pull request.
- **Deployment**: One release of a specific verified commit to the Azure environment, with an owner-visible status, health outcome, and history.
- **Environment configuration**: The named set of non-secret values and protected secrets (resource names, endpoints, tenant and client identifiers) the pipeline and deployed application need, held outside source control.
- **Pipeline identity**: The Azure identity GitHub uses to deploy, the repository/branch/environment it trusts, and the narrow permissions it holds on the mapped resources.
- **Resource mapping**: The documented correspondence between each application component and the existing Azure resource reused for it, including gaps.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of pull requests to `main` receive build, test, and migration check results automatically, and a pull request with any failing required check cannot be merged.
- **SC-002**: A typical pull request shows all verification results within 15 minutes of the last push.
- **SC-003**: A merged change that passes verification is live in the Azure environment, healthy, within 20 minutes of the merge with no manual step other than the owner's approval of the deployment when the environment requires one.
- **SC-004**: A deliberately unhealthy release leaves the previous version serving users, and the failure is reported within the run in 100% of trials.
- **SC-005**: Zero Azure passwords, keys, or client secrets are present in the repository, its workflow files, or run logs, and a scan of the repository finds none.
- **SC-006**: The owner can return the environment to any previously deployed commit with one manual action and no more than 20 minutes of waiting.
- **SC-007**: Every application component maps to an existing Azure resource or to a documented gap, and repeating the one-time setup produces no changes.
- **SC-008**: No existing Azure resource is deleted or replaced by the initial setup or by any of 10 consecutive deployments.
- **SC-009**: After a deployment, a signed-in user can open the application and use vehicle features, and the deferred email-intake walkthrough can be completed against the deployed environment.

## Assumptions

- The source code is hosted in the existing GitHub repository `TK11703/my-garage`, and the owner can administer its settings (branch protection, environments, variables) and has rights to configure identity and permissions in the target Azure subscription and Microsoft Entra tenant.
- There is a single deployed environment (the one built from the owner's existing Azure resources) that receives every merge to `main`. A separate staging environment is out of scope for now; the deployment-environment gate (FR-013) leaves room to add an approval step or more environments later. The `production` environment already exists with the owner as its required reviewer (self-approval allowed, since the owner is the only maintainer), so each deployment waits for the owner's approval; the 20 minutes of SC-003 are counted from that approval.
- The existing Azure resources cover, at least in part, the application's hosting, PostgreSQL database, private blob storage, extraction (Azure OpenAI) service, and monitoring. Which resource covers which component, and any gaps, is established during planning from the actual subscription; where something is missing the feature reports the gap instead of inventing a resource.
- Creating new infrastructure from scratch is out of scope; only the small, repeatable preparation needed to let the pipeline and the application use the existing resources is in scope. The new items the deployment adds are limited to the application's own components (its two container apps, its identities, one storage account, and one database on the shared server).
- The deployed environment uses its own Microsoft Entra app registrations, separate from the local-development registrations. Database access in the deployed environment uses Microsoft Entra authentication rather than a stored password, and the web application proves its identity to Microsoft Entra with a managed-identity federated credential rather than a client secret; local development keeps using a password and a client secret. Defender for Storage is not enabled.
- The web application's Entra registration will have, or be updated to have, redirect addresses for the deployed web address; changing the registration is part of one-time preparation and does not alter application behavior.
- The tests that need a real database run on GitHub-hosted runners that provide a container runtime; no self-hosted runners are required.
- The Logic App that feeds email intake is managed outside this pipeline and is not deployed by it, although it will be pointed at the deployed API address.
- Application behavior, the data model, and public API contracts are unchanged by this feature.
