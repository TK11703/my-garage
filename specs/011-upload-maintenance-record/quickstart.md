# Quickstart: Upload a Maintenance Record

Validation guide for feature 011. Contracts: [api.openapi.yaml](contracts/api.openapi.yaml), [web-routes.md](contracts/web-routes.md). Data: [data-model.md](data-model.md).

## Prerequisites

- Docker running (Testcontainers PostgreSQL and Azurite).
- .NET SDK from `global.json`.
- For the manual run: `aspire run`, a signed-in user with at least one vehicle, and an AI deployment configured in the extraction options (without one, uploads still work but every file becomes a manual-entry draft).
- Do not run tests while `aspire run` is active.

## Automated checks

```powershell
dotnet build MyGarage.slnx
dotnet test tests\MyGarage.Database.Tests --filter "FullyQualifiedName~EmailIntake|FullyQualifiedName~Migration|FullyQualifiedName~SqlScriptStore"
dotnet test tests\MyGarage.Api.Tests --filter "FullyQualifiedName~Drafts|FullyQualifiedName~EmailIntake"
dotnet test tests\MyGarage.Web.Tests
```

Expected: zero warnings, all pass. The existing email intake tests must pass unchanged (SC-006).

## Manual scenarios

| # | Steps | Expected |
|---|---|---|
| 1 | On the main screen choose **Add Maintenance**, select a service-invoice PDF, **Upload**. | Redirect to the drafts list with a notice that the file is being read. Within about two minutes a draft appears and the bell shows a notification. No record exists yet. (US1, FR-008, FR-011) |
| 2 | Open the draft. | Proposed service record with line items beside the original PDF, compatibility panel, "Uploaded" block with the file name. (FR-012, FR-021) |
| 3 | Pick a vehicle, include it, keeping the original. | Record created on that vehicle with the from-email mark; the original appears under the vehicle's Documents; draft leaves the list. (FR-013, FR-014) |
| 4 | Open a vehicle, go to the Maintenance tab, choose **Import Record**, upload an invoice. | Page names the vehicle; the resulting draft has that vehicle chosen with the explanation "You chose this vehicle when you uploaded the file." (US2, FR-010) |
| 5 | Open `/drafts/upload?vehicleId=` with another user's vehicle id. | Not-found message; nothing stored. (FR-010, FR-017) |
| 6 | Upload a `.txt` file renamed `.pdf`, an empty file, and an 11 MB file. | Each refused by name with a reason; nothing stored. (US3, FR-004) |
| 7 | Select three files, one unsupported. | The page stays open, says two files were uploaded, and names the unsupported one with its reason; two drafts appear in the list. (US4) |
| 8 | Upload a valid but unreadable image. | A "Needs details" draft with the original viewable. (FR-009) |
| 9 | Upload the same invoice twice. | Two drafts; the second shows the existing duplicate warning. (Edge case) |
| 10 | Open the History tab on `/drafts`. | The upload shows its outcome and an Uploaded label; included items link to the record. (US5, FR-023) |
| 11 | Sign in as a second user, request the first user's draft and attachment. | Not found. (FR-017, FR-018) |
| 12 | Send more than the per-minute upload limit. | A rate-limit message with a retry hint. (FR-005) |

## Cleanup check

After discarding or expiring an uploaded draft, its blob in the `email-intake` container is gone (the existing expiry and orphan sweep tests cover this for the new origin).
