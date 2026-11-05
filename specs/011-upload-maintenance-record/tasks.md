---

description: "Task list for Upload a Maintenance Record"
---

# Tasks: Upload a Maintenance Record

**Input**: Design documents from `/specs/011-upload-maintenance-record/`

**Prerequisites**: plan.md, spec.md (with the 2026-11-05 refinement), research.md (R1-R12), data-model.md, contracts/api.openapi.yaml, contracts/web-routes.md, quickstart.md

**Tests**: Required (constitution VIII): write the test, run it, watch it fail for the right reason, then implement. The database and blob storage are never mocked (Testcontainers PostgreSQL and Azurite); only the AI boundary is faked (the existing fake `IDraftExtractor`).

**Organization**: Grouped by user story. US1 upload and review (P1, MVP); US2 "Import Record" with the vehicle preselected (P2); US3 files that cannot be used (P2); US4 several files at once (P3); US5 origin labels (P3). The Foundational phase holds the shared DTOs, the migration, the repository changes, and the shared attachment helper every story reads. The upload endpoint is built once in US1 with all safety checks (type from bytes, size, count, ownership); later stories add the preselected vehicle, the Web messaging, the rate limit, and the labels.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1-US5, only on user-story phase tasks
- Names to keep identical everywhere: endpoint `POST /api/drafts/uploads` (operation `UploadDraftFiles`, multipart fields `files` and `vehicleId`); request type `DraftUploadRequest`; DTOs `DraftUploadResultDto`, `DraftUploadRefusalDto` (`Index`, `Reason`), `DraftOrigin` (`Email`, `Upload`, JSON `email`, `upload`), `VehicleMatch.Selected` (JSON `selected`), `DraftUploadLimits`; reason codes `unsupported_type`, `empty`, `too_large`; Web page `/drafts/upload` with named form `upload-record`, file field `recordFiles`, query value `vehicleId`; list notice query value `uploaded`; rate-limit policy `draft-upload` and settings section `DraftUploads` (`RequestsPerMinute`, default 10); synthetic message id prefix `upload:`; migration `0015_add_upload_origin`
- Limits (shared constants in `DraftUploadLimits`, equal to the email limits): at most 5 files per upload, 10,485,760 bytes per file, 15,728,640 bytes in total; types `application/pdf`, `image/jpeg`, `image/png`, `image/webp`
- No new NuGet package; no new project; no new table or storage container; records and their DTOs (`FromEmail`) are not changed
- Never run `dotnet test` while `aspire run` is active; Docker must be running for Database and Api tests
- Work on branch `011-upload-maintenance-record`; never commit to `main`; merge by pull request

## Phase 1: Setup

- [X] T001 Confirm the workspace is on branch `011-upload-maintenance-record` (`git branch --show-current`; create it from `main` if not), then run `dotnet build MyGarage.slnx` (zero warnings) and `dotnet test tests/MyGarage.Web.Tests` to record the baseline; run `tests/MyGarage.Database.Tests` and `tests/MyGarage.Api.Tests --filter "FullyQualifiedName~EmailIntake|FullyQualifiedName~Drafts"` once as the email-intake regression baseline (SC-006)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared contracts, the schema, the repository columns, and the attachment helper shared by email and upload. Nothing in the stories compiles against these until they exist.

- [X] T002 [P] Add the Shared contract types and extend existing ones, per data-model.md, with these constraints verbatim: `src/MyGarage.Shared/Drafts/DraftOrigin.cs` (`Email`, `Upload`; JSON text `email`, `upload` using the existing `SnakeCaseEnumConverter<T>`); `src/MyGarage.Shared/Drafts/DraftUploadLimits.cs` (`MaxFiles` = 5, `MaxFileBytes` = 10,485,760, `MaxTotalBytes` = 15,728,640, accepted content types and an `Accept` string for the file input, reason-code constants `unsupported_type`, `empty`, `too_large`); `src/MyGarage.Shared/Drafts/DraftUploadResultDto.cs` (`SubmissionId` guid, `AcceptedCount` int at least 1, `Refused` list of `DraftUploadRefusalDto` with `Index` int at least 0 = position of the file in the request and `Reason` string from the three codes); add member `Selected` to `src/MyGarage.Shared/Drafts/VehicleMatch.cs` (JSON `selected`); add `Origin` (`DraftOrigin`, defaults to `Email`) to `MaintenanceDraftDto` and `MaintenanceDraftSummaryDto` in `src/MyGarage.Shared/Drafts/` and to `EmailHistoryItemDto` in `src/MyGarage.Shared/EmailIntake/EmailHistoryItemDto.cs`
- [X] T003 [P] Write failing tests in `tests/MyGarage.Database.Tests/EmailIntake/UploadOriginMigrationTests.cs` (real PostgreSQL, helpers as in `MaintenanceMigrationTests.cs`): after migration `0015_add_upload_origin`, `email_submissions.origin` is `varchar(6) NOT NULL DEFAULT 'email'` and accepts only `email` and `upload`; rows inserted without it read `email`; `email_submissions.preselected_vehicle_id` is `uuid NULL`, references `vehicles (id)`, and becomes NULL when that vehicle is deleted (`ON DELETE SET NULL`); `maintenance_drafts.vehicle_match` accepts `vin`, `description`, `only_vehicle`, `selected`, and `none` and rejects anything else; migrations `0001` to `0014` checksums are unchanged
- [X] T004 Create `src/MyGarage.Database/Scripts/Migrations/0015_add_upload_origin.sql` (forward-only): add `email_submissions.origin varchar(6) NOT NULL DEFAULT 'email' CHECK (origin IN ('email', 'upload'))`; add `email_submissions.preselected_vehicle_id uuid NULL REFERENCES vehicles (id) ON DELETE SET NULL`; `ALTER TABLE maintenance_drafts DROP CONSTRAINT maintenance_drafts_vehicle_match_check` and re-add it as a named constraint allowing `'vin', 'description', 'only_vehicle', 'selected', 'none'`. Bump the migration expectations: `tests/MyGarage.Database.Tests/Common/MigrationRunnerTests.cs` (ledger count and the new `ledger[14]` = `0015_add_upload_origin`, and the expected script list near line 948) and the script list in `tests/MyGarage.Database.Tests/Common/SqlScriptStoreTests.cs`. Run T003 and both files
- [X] T005 [P] Write failing tests: in `tests/MyGarage.Database.Tests/EmailIntake/EmailSubmissionRepositoryTests.cs` — `AddReceivedAsync` stores `Origin` = `upload` with a null sender, subject, and body; `PreselectedVehicleId` is kept only when the vehicle belongs to the submission's owner (another owner's vehicle or an unknown id is stored as NULL, and the submission still inserts); a submission without these values reads `email` and no preselected vehicle; in `EmailSubmissionQueueTests.cs` — `ClaimNextAsync` returns `Origin` and `PreselectedVehicleId`; in `EmailHistoryRepositoryTests.cs` — each history record carries its origin; in `tests/MyGarage.Database.Tests/Drafts/MaintenanceDraftRepositoryTests.cs` — the draft read by id and each row of the open-drafts page carry the submission's origin
- [X] T006 Implement to make T005 pass: add `Origin` (string, default `email`) and `PreselectedVehicleId` (`Guid?`) to `src/MyGarage.Database/EmailIntake/EmailSubmissionRecord.cs`; extend `Scripts/EmailIntake/InsertSubmission.sql` with `@Origin` and `@PreselectedVehicleId` (the vehicle kept only through `(SELECT v.id FROM vehicles v WHERE v.id = @PreselectedVehicleId AND v.owner_id = @OwnerId)`; leave the `ON CONFLICT` update list unchanged), `Scripts/EmailIntake/ClaimNext.sql` (return both columns), `Scripts/EmailIntake/ListHistoryPage.sql` plus the `EmailHistoryRecord`/`HistoryRow` mapping in `EmailSubmissionRepository.cs` (origin), `Scripts/Drafts/GetById.sql` and `Scripts/Drafts/ListOpenPage.sql` plus the read records in `src/MyGarage.Database/Drafts/MaintenanceDraftReadRecords.cs` (origin). No SQL in C#; all parameterized; async with `CancellationToken`. Run T005 and the existing Database tests
- [X] T007 [P] Write failing tests in `tests/MyGarage.Api.Tests/EmailIntake/IntakeAttachmentsTests.cs` for the shared helper `IntakeAttachments.Prepare(submissionId, ownerId, now, files)` (files = display name and bytes): a PDF, JPEG, PNG, and WebP are recognized from their leading bytes whatever the name says; a `.pdf` name on text content is skipped with reason `unsupported_type`; an empty file is skipped with reason `empty`; a file over `DraftUploadLimits.MaxFileBytes` is skipped with reason `too_large`; each stored record has a new version-7 id, the owner, `SizeBytes`, lower-case hex SHA-256, and a sanitized display name (path parts and control characters dropped, empty becomes `attachment`); the skipped list carries the input position and reason; nothing in the result contains file content
- [X] T008 Create `src/MyGarage.Api/EmailIntake/IntakeAttachments.cs` by extracting the detect, hash, `SafeFileName`, record-building, and upload-then-cleanup logic from `src/MyGarage.Api/EmailIntake/IntakeService.cs` (`StoredAttachment`, `SkippedAttachment`, `ReadAttachments`, `SafeFileName`, `DeleteBlobsAsync` best-effort cleanup via `IIntakeBlobStore`), add the `empty` and `too_large` checks, and make `IntakeService` call it with no behavior change (an email's unsupported type still lists as `unsupported_type`, so the email checks must stay green). Run T007, then the full existing `tests/MyGarage.Api.Tests/EmailIntake` suite unchanged

**Checkpoint**: Shared and Database compile and their tests pass; the email intake suites are green; API and Web behavior is unchanged until the stories below.

---

## Phase 3: User Story 1 - Upload a record file and review what was found (Priority: P1) MVP

**Goal**: A signed-in user chooses "Add Maintenance" on the main screen, uploads a PDF or image, and the file becomes an ordinary draft (with notification) that is reviewed, included, or discarded exactly like an email draft. Nothing becomes a record until the user includes it.

**Independent Test**: Quickstart scenarios 1, 2, 3 and 11: upload an invoice, see the draft and notification, review beside the original, include it keeping the original, and confirm another user gets not-found.

### API: validation, service, endpoint

- [X] T009 [P] [US1] Write failing tests in `tests/MyGarage.Api.Tests/Drafts/DraftUploadRequestValidatorTests.cs`: no files (null or empty) fails on `files`; 1 and 5 files pass; 6 files fail; a total of 15,728,640 bytes passes and one byte more fails; a null entry in the list fails on `files[i]`; `vehicleId` of `Guid.Empty` fails; a null `vehicleId` passes. Errors are reported on the property path (`files`, `files[i]`, `vehicleId`) so the endpoint answers a standard validation problem
- [X] T010 [US1] Create `src/MyGarage.Api/Drafts/Uploads/DraftUploadRequest.cs` (`[FromForm]` type with `IFormFileCollection`/`List<IFormFile>? Files` and `Guid? VehicleId`, same pattern as `src/MyGarage.Api/Vehicles/StickerImport/StickerUploadRequest.cs`) and `DraftUploadRequestValidator.cs` using `DraftUploadLimits`. Run T009
- [X] T011 [P] [US1] Write failing tests in `tests/MyGarage.Api.Tests/Drafts/DraftUploadEndpointsTests.cs` (`ApiFactory` with real PostgreSQL and Azurite, fake `IDraftExtractor`, test JWT helpers as in `IntakeEndpointTests.cs` and `DraftsEndpointsTests.cs`):
  - a signed-in user posts one PDF and gets `202` with `DraftUploadResultDto` (`AcceptedCount` 1, empty `Refused`); `email_submissions` has one `queued` row with `origin` = `upload`, `message_id` starting `upload:`, `owner_id` from the token, null `sender`, `subject`, and `body_text`; one `email_attachments` row for the owner with the detected type; the blob exists in the intake container; no draft, vehicle, or record row exists yet
  - running the processor once (the helper `SubmissionProcessorTests` uses) turns it into one `needs_review` draft owned by the caller, with a `draft_ready` notification, and `GET /api/drafts/{id}` returns `Origin` upload, `HasAttachment` true, and the original through `GET /api/drafts/{id}/attachment`
  - accepting that draft with a chosen vehicle and keep-original creates the record with `FromEmail` true, stores the original in the vehicle documents, and removes the intake blob; discarding creates nothing and removes the blob
  - a second user gets 404 for that draft and its attachment
  - no `files` field → `400` validation problem and nothing stored; a text file named `invoice.pdf` → `400` with `errors["files[0]"]` and nothing stored; a PNG and a JPEG are accepted; an empty file and a 10,485,761-byte file are each refused (`400` when nothing is accepted)
  - no token → `401`; an app-only token with the `Email.Ingest` role → `403`; the intake endpoint still refuses user tokens (regression)
  - an unavailable blob store (existing storage-failure test double) → `503` with `Retry-After` and no submission row
- [X] T012 [US1] Create `src/MyGarage.Api/Drafts/Uploads/DraftUploadService.cs` (singleton: `UploadAsync(ownerId, files, vehicleId?, ct)`): reads each `IFormFile` into bytes (never more than `MaxFileBytes + 1` per file), calls `IntakeAttachments.Prepare`, refuses with a validation problem keyed `files[i]` when nothing is stored, otherwise uploads the blobs, then `EmailSubmissionRepository.AddReceivedAsync` with a new submission (version-7 id, `MessageId` = `upload:{id}`, `ArrivedAt` and `ReceivedAt` = now from `TimeProvider`, `OwnerId` from claims, `Origin` = `upload`, sender, subject, and body null, `SkippedAttachments` `[]`), and deletes its own blobs if the row insert fails (as `IntakeService.AcceptAsync` does). Logs only the submission id and counts, never file names. Return `DraftUploadResultDto` with refused indexes mapped back to the request order. Run the service-level parts of T011
- [X] T013 [US1] Add `MapPost("/uploads", ...)` to `src/MyGarage.Api/Drafts/DraftsEndpoints.cs` in the existing `/api/drafts` group (so `DraftStorageFilter` maps storage failures to `503`): owner from `CurrentOwner.Get(user)` (else `VehiclesEndpoints.NoOwner()`), `.DisableAntiforgery()`, `.Accepts<DraftUploadRequest>("multipart/form-data")`, `RequestSizeLimitAttribute` of 16,777,216 bytes (15 MB of files plus the multipart envelope), `ValidationFilter<DraftUploadRequest>`, name `UploadDraftFiles`, `Produces<DraftUploadResultDto>(202)`, `ProducesValidationProblem`, and 401, 403, 404, 413, 429, 503 problems as in contracts/api.openapi.yaml; register the validator and `DraftUploadService` in `AddDrafts`. Run T011 and the existing `DraftsEndpointsTests`

### Web: client, handler, page, main-screen button

- [X] T014 [P] [US1] Write failing tests in `tests/MyGarage.Web.Tests/Drafts/DraftUploadHandlerTests.cs` (fake `HttpMessageHandler` behind `DraftsApiClient`, helpers as in `DraftTestContext.cs`): a post with no file, with 6 files, with a file over 10,485,760 bytes, or over 15,728,640 bytes in total is refused before any Api call, each with its own message; valid files are streamed to `POST api/drafts/uploads` as multipart field `files` without the user's file names; an Api `202` returns the accepted count and the refused entries; a `401` or `403` throws `GarageApiException(NotAccepted)`; a `5xx` or network failure throws `GarageApiException(Unavailable)`
- [X] T015 [US1] (Built as its own client `DraftUploadApiClient` in `src/MyGarage.Web/Drafts/DraftUploadApiClient.cs`, registered with a 2 minute timeout and no retry like the sticker client, because a retry would queue the files twice.) Add `UploadAsync(IReadOnlyList<UploadContent> files, Guid? vehicleId, CancellationToken)` (returns a `DraftUploadResponse` record with the result or a `DraftUploadFailure` of `Invalid`, `TooLarge`, `NotFound`, `RateLimited`; follows the pattern of `StickerImportApiClient`, including the `MicrosoftIdentityWebChallengeUserException` mapping) and create `src/MyGarage.Web/Drafts/DraftUploadHandler.cs` (reads `Request.Form.Files` field `recordFiles`, applies the limits from `DraftUploadLimits`, same shape as `StickerImportHandler`) with the user-facing messages in one `DraftUploadMessages` class. Run T014
- [X] T016 [P] [US1] Write failing tests in `tests/MyGarage.Web.Tests/Drafts/DraftUploadPageTests.cs` (bUnit, `DraftTestContext`): the page at `/drafts/upload` renders the header with the trail "Maintenance Drafts > Add maintenance", a `<form method="post" enctype="multipart/form-data">` named `upload-record` with an antiforgery token, a visually hidden label, a file input named `recordFiles` with `multiple`, `required`, `accept` from `DraftUploadLimits.Accept`, and help text naming the file types and limits ("up to 5 files, 10 MB each, 15 MB in total"), the sentence "Nothing becomes a record until you review and include it", a submit button with busy text, and a note that reading happens after the upload; no hidden `vehicleId` field and no vehicle name without a query value; the Api sign-in and outage failures render the standard `ApiFailureAlert`
- [X] T017 [P] [US1] Write failing tests in `tests/MyGarage.Web.Tests/Drafts/DraftUploadSsrTests.cs` (`WebAppFactory`, AngleSharp, `ConfigureTestServices(AddSingleton(client))` as in `DraftReviewSsrTests.cs`): a real multipart post of one PDF with the antiforgery token and the fake Api answering `202` redirects to `/drafts?uploaded=1`; a post without the antiforgery token is refused; an Api `202` with an unknown file name never echoes it into the redirect address
- [X] T018 [US1] Create `src/MyGarage.Web/Drafts/DraftUploadPage.razor` (`@page "/drafts/upload"`, `[Authorize]`, `[ExcludeFromInteractiveRouting]`, named SSR form `upload-record` with `@onsubmit`, dropzone styling as in `StickerImportPanel.razor` and `VehicleGallerySection.razor`, `PageHeader` with `Crumb`s, messages through `DraftUploadMessages`); on a full success redirect to `/drafts?uploaded={acceptedCount}`. Follow `docs/ui-standard.md` (semantic HTML, accessible names, Bootstrap utilities only). Run T016 and T017
- [X] T019 [P] [US1] Write failing tests: in `tests/MyGarage.Web.Tests/Drafts/DraftsListPageTests.cs` — `/drafts?uploaded=2` shows a `role="status"` notice "2 files uploaded. They are being read; each draft appears below and in your notifications when ready." (singular for 1); a non-numeric, zero, negative, or huge `uploaded` value shows no notice and never breaks the page; the empty-state text mentions uploading; the header has an "Add maintenance" link to `/drafts/upload`; in `tests/MyGarage.Web.Tests/Home/HomeTests.cs` — an "Add Maintenance" link to `/drafts/upload` is present in the hero button row both when the user has vehicles and when they have none
- [X] T020 [US1] Add the `uploaded` notice, the header link, and the empty-state wording to `src/MyGarage.Web/Drafts/DraftsListPage.razor` (read as text, parse digits, clamp to 1 through 5), and the "Add Maintenance" link button (`btn btn-outline-primary btn-lg d-inline-flex align-items-center gap-2` with the existing upload icon) to `src/MyGarage.Web/Home/Home.razor` in both button-row branches. Run T019
- [X] T021 [US1] Extend `tests/MyGarage.Web.Tests/Common/UiStandardConformanceTests.cs` expectations if the scanner lists pages explicitly, and run it for the new page; register nothing else (the page is routed by `@page`). Run `dotnet test tests/MyGarage.Web.Tests`

**Checkpoint**: Quickstart scenarios 1, 2, 3 and 11 work end to end. Email intake behavior is unchanged.

---

## Phase 4: User Story 2 - Import a record from a vehicle's maintenance tab (Priority: P2)

**Goal**: "Import Record" in the vehicle's maintenance tab uploads with the vehicle already chosen on the draft.

**Independent Test**: Quickstart scenarios 4 and 5.

- [X] T022 [P] [US2] Write failing tests in `tests/MyGarage.Api.Tests/EmailIntake/VehicleMatcherTests.cs`: with a preselected vehicle that is among the candidates, `VehicleMatcher.Match(vehicles, hint, preselectedVehicleId)` returns that vehicle with `VehicleMatch.Selected` even when the hint's VIN or make and model point to a different vehicle; a preselected id that is not a candidate falls back to the existing VIN, description, only-vehicle, then none order; a null preselected id behaves exactly as before; and in `tests/MyGarage.Api.Tests/Drafts/DraftServiceTests.cs` — `DraftKinds` maps `selected` to and from `VehicleMatch.Selected` and the draft's vehicle candidates list marks it
- [X] T023 [US2] Add the optional `preselectedVehicleId` parameter to `VehicleMatcher.Match` in `src/MyGarage.Api/EmailIntake/Extraction/VehicleMatcher.cs`, and the `selected` text mapping in `src/MyGarage.Api/Drafts/DraftKinds.cs` (both `ToText` and the parse). Run T022
- [X] T024 [P] [US2] Write failing tests in `tests/MyGarage.Api.Tests/EmailIntake/SubmissionProcessorTests.cs`: an upload submission with `PreselectedVehicleId` produces drafts whose `vehicle_id` is that vehicle and `vehicle_match` is `selected`, for a readable file and for a manual-entry draft (unreadable file); the extractor's VIN hint for another vehicle does not override it; a preselected vehicle deleted before processing leaves the draft with the normal match result; an email submission (no preselected vehicle) is unchanged
- [X] T025 [US2] Pass `submission.PreselectedVehicleId` to the matcher in `NewDraft` and `NewManualEntryDraft` in `src/MyGarage.Api/EmailIntake/SubmissionProcessor.cs`. Run T024 and the full `SubmissionProcessorTests` and `SubmissionProcessorConcurrencyTests`
- [X] T026 [P] [US2] Add failing tests to `tests/MyGarage.Api.Tests/Drafts/DraftUploadEndpointsTests.cs`: posting with `vehicleId` of the caller's vehicle stores `preselected_vehicle_id` and, after processing, the draft shows that vehicle with `Match` `Selected`; a vehicle id of another user, and an unknown id, each answer `404` and store no submission, attachment, or blob; the answer for another user's vehicle is identical to that for an unknown one
- [X] T027 [US2] In `DraftUploadService` (`src/MyGarage.Api/Drafts/Uploads/DraftUploadService.cs`) look up the vehicle with the owner-scoped `VehicleRepository.GetByIdAsync(ownerId, vehicleId, ct)` before anything is read or stored, return not-found (`VehiclesEndpoints.NotFound()`) when null, and set `PreselectedVehicleId` on the submission. Run T026
- [X] T028 [P] [US2] Write failing tests: in `tests/MyGarage.Web.Tests/Maintenance/MaintenanceViewTests.cs` — the maintenance view shows an "Import Record" link (accessible name "Import a record for this vehicle", with the upload icon) above the accordion with `href="/drafts/upload?vehicleId={vehicleId}"`; in `tests/MyGarage.Web.Tests/Drafts/DraftUploadPageTests.cs` — with `?vehicleId={id}` the page asks `VehiclesApiClient` for the vehicle and shows "Importing a record for {name}" (name escaped), carries a hidden `vehicleId` field in the form, and passes the id to the Api in the handler; a malformed `vehicleId` is ignored (no vehicle, no hidden field); a vehicle the Api reports as not found shows the not-found message with a link to the vehicle list and no form; in `tests/MyGarage.Web.Tests/Drafts/DraftReviewPageTests.cs` — a draft with `Match` `Selected` shows "You chose this vehicle when you uploaded the file."
- [X] T029 [US2] Implement: add the "Import Record" link to `src/MyGarage.Web/Maintenance/MaintenanceView.razor` (a right-aligned `btn btn-outline-primary btn-sm` above the accordion); read `vehicleId` (as text, `Guid.TryParse`) and the vehicle name in `src/MyGarage.Web/Drafts/DraftUploadPage.razor` and pass it through `DraftUploadHandler`/`DraftsApiClient.UploadAsync` (field `vehicleId`); add `VehicleMatch.Selected` to `DraftFormat.MatchNote` in `src/MyGarage.Web/Drafts/DraftFormat.cs`. Run T028 and `tests/MyGarage.Web.Tests`

**Checkpoint**: Quickstart scenarios 4 and 5 work.

---

## Phase 5: User Story 3 - Handle files that cannot be used (Priority: P2)

**Goal**: Bad files are refused clearly and immediately with nothing stored; a valid but unreadable file becomes a manual-entry draft; failures and abuse are bounded.

**Independent Test**: Quickstart scenarios 6, 8 and 12.

- [X] T030 [P] [US3] Write failing tests in `tests/MyGarage.Web.Tests/Drafts/DraftUploadPageTests.cs`: after a post whose Api answer is `400` (nothing accepted) the page stays open and shows an alert (`role="alert"`) listing each refused file by the name the user's browser sent (escaped, including `<script>` in a name) with the reason text ("must be a PDF, JPG, PNG, or WebP", "is empty", "is larger than 10 MB"); choosing no file shows "Choose at least one file"; the handler's own limit messages (too many files, too large, total too large) show in the same alert; an Api `429` shows "Too many uploads. Try again in a minute."; an Api `404` for the vehicle shows the not-found message; an outage shows the standard unavailable alert with a retry link; the page never shows an Api URL or exception text
- [X] T031 [US3] Implement the refusal rendering in `src/MyGarage.Web/Drafts/DraftUploadPage.razor` and `DraftUploadMessages.cs` (map `Refused[i]` or the `files[i]` validation error index back to the names of the posted files), keeping the form visible so the user can pick again. Run T030
- [X] T032 [P] [US3] Write failing tests in `tests/MyGarage.Api.Tests/Drafts/DraftUploadRateLimitTests.cs` (a factory with `DraftUploads:RequestsPerMinute` = 2): the third upload in a minute by one user answers `429` with `Retry-After` and a `ProblemDetails` body and stores nothing; a different user is not limited by the first; the limit is per signed-in user, not per address; the other drafts endpoints and the intake endpoint are not affected
- [X] T033 [US3] Create `src/MyGarage.Api/Common/DraftUploadRateLimiting.cs` (policy `draft-upload`, partition `user:{ownerId}` else `ip:{address}`, fixed window one minute, limit from `DraftUploadOptions.RequestsPerMinute`, rejection written by `IntakeRateLimiting.WriteRejectionAsync`; same shape as `StickerImportRateLimiting.cs`), `src/MyGarage.Api/Drafts/Uploads/DraftUploadOptions.cs` (section `DraftUploads`, `RequestsPerMinute` default 10, at least 1), register them in `AddDrafts`, add `.RequireRateLimiting(DraftUploadRateLimiting.PolicyName)` to the upload endpoint, and set `["DraftUploads:RequestsPerMinute"] = "1000"` in `tests/MyGarage.Api.Tests/Common/ApiFactory.cs` next to the sticker setting. Run T032
- [X] T034 [P] [US3] Add failing tests: in `tests/MyGarage.Api.Tests/EmailIntake/SubmissionProcessorTests.cs` — an uploaded valid file the extractor cannot read (content-filter failure, or a low-confidence non-maintenance answer) yields a `manual_entry` draft with the original viewable, and an upload whose extraction is disabled yields manual-entry drafts for every file; in `tests/MyGarage.Api.Tests/EmailIntake/LogContentTests.cs` — an upload with an identifying file name and content writes no file name, content, owner id, or extracted value to the logs (only ids and counts); in `tests/MyGarage.Api.Tests/Drafts/DraftUploadEndpointsTests.cs` — a request over 16,777,216 bytes answers `413` and stores nothing
- [X] T035 [US3] Make T034 pass: fix any gap found in `SubmissionProcessor`, `DraftUploadService`, or the endpoint (expected: none for the processor, which already handles unreadable files; the log and size checks may need a log call adjusted to ids and counts only). Run T034 and the full `tests/MyGarage.Api.Tests/EmailIntake` suite

**Checkpoint**: Quickstart scenarios 6, 8 and 12 work; no stored file or log line for a refused file.

---

## Phase 6: User Story 4 - Upload several files at once (Priority: P3)

**Goal**: One upload of up to five files yields one draft per good file; bad files do not block the others and are named on the page.

**Independent Test**: Quickstart scenario 7.

- [X] T036 [P] [US4] Add tests to `tests/MyGarage.Api.Tests/Drafts/DraftUploadEndpointsTests.cs` (regression for behavior built in T012; they must pass without new server code, and any failure is fixed in `DraftUploadService`): three valid files produce one submission with three attachments and, after one processor pass, three drafts; a mix of two valid and one unsupported file answers `202` with `AcceptedCount` 2 and `Refused` holding index 1 with reason `unsupported_type`, and only two blobs exist; six files answer `400` and store nothing; files totalling over 15,728,640 bytes answer `400` and store nothing; each draft can be accepted or discarded independently of the others
- [X] T037 [P] [US4] Write failing tests in `tests/MyGarage.Web.Tests/Drafts/DraftUploadPageTests.cs`: when the Api answers `202` with refused entries the page does not redirect; it shows a `role="status"` notice ("2 files uploaded and are being read", with a link "View maintenance drafts" to `/drafts`) and an alert naming each refused file by its own name (escaped) and reason; when nothing is refused the page redirects with `uploaded` set to the accepted count
- [X] T038 [US4] Implement the partial-success rendering in `src/MyGarage.Web/Drafts/DraftUploadPage.razor` (the SSR post handler decides between redirect and in-page result). Run T037

**Checkpoint**: Quickstart scenario 7 works.

---

## Phase 7: User Story 5 - See where a draft came from (Priority: P3)

**Goal**: Uploaded drafts are labelled as uploads in the drafts list, the review page, and the history; records keep the existing from-email mark.

**Independent Test**: Quickstart scenarios 2 (upload block) and 10.

- [X] T039 [P] [US5] Write failing tests: in `tests/MyGarage.Api.Tests/Drafts/DraftServiceTests.cs` and `DraftsEndpointsTests.cs` — the draft detail and each summary row carry `Origin` `Upload` for an upload submission and `Email` for an email one; an open upload draft's `Email` block holds only `ArrivedAt` (no sender or subject), and an email draft's block is unchanged; in `tests/MyGarage.Api.Tests/EmailIntake/EmailHistoryEndpointsTests.cs` — each history item carries `Origin` and still carries no sender, subject, or text; in `tests/MyGarage.Api.Tests/Drafts/DraftMapperOriginTests.cs` — the mapper maps the stored origin text `email`/`upload` to `DraftOrigin` and an unknown text to `Email`
- [X] T040 [US5] Implement in `src/MyGarage.Api/Drafts/DraftMapper.cs` (draft, summary, and `Email` block as above) and the history mapping in `src/MyGarage.Api/EmailIntake/EmailHistoryEndpoints.cs`/its mapper. Run T039
- [X] T041 [P] [US5] Write failing tests in `tests/MyGarage.Web.Tests/Drafts/DraftsListPageTests.cs` and `DraftReviewPageTests.cs`: an upload draft row and an upload history row show an "Uploaded" badge (`data-testid="draft-origin"`, `history-origin`) and email rows show none; the Original card of an upload draft shows an "Uploaded" block with the upload time and `draft.Source.FileName` (escaped) in place of From and Subject, and a draft from email text still shows the existing text-source note; an upload draft's history empty-state wording mentions uploads; the vehicle's maintenance lists still show the existing "From email" badge for a record created from an upload (unchanged)
- [X] T042 [US5] Implement the labels in `src/MyGarage.Web/Drafts/DraftsListPage.razor`, `DraftReviewPage.razor`, and `DraftFormat.cs`. Run T041 and `tests/MyGarage.Web.Tests`

**Checkpoint**: Quickstart scenarios 2 and 10 work.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T043 [P] Update the help and docs: add an entry about uploading a maintenance record (Add Maintenance, Import Record, the review step, limits) to the FAQ content under `src/MyGarage.Web/Help/` (with its test if the FAQ is test-covered), a short note in `docs/README.md` if it lists features, and a settings note for `DraftUploads:RequestsPerMinute` wherever `StickerImport:RequestsPerMinute` is documented (`src/README.md` or `infra/README.md`; search first)
- [X] T044 Run the whole suite and gates: `dotnet build MyGarage.slnx` with zero warnings, `dotnet test MyGarage.slnx` (all pass, including every unchanged email-intake test), and confirm migrations apply cleanly to an empty database (the Database migration tests do this)
- [X] T045 Run the quickstart manual scenarios 1-12 against `aspire run`: ask the user to sign in, then drive the integrated browser through each scenario; record anything that differs from `quickstart.md` and fix it or the document

---

## Dependencies & Execution Order

- **Phase 1** first. **Phase 2** blocks everything: T002 (Shared) and T003/T005/T007 (failing tests) can start together; T004 follows T003; T006 follows T004 and T005; T008 follows T007.
- **US1 (Phase 3)** needs Phase 2. Inside it: T009, T011, T014, T016, T017, and T019 are independent test files; T010 then T012 then T013 build the API (T013 needs T012); T015 needs T013; T018 needs T015 and T016; T020 needs T019 and T018; T021 last.
- **US2 (Phase 4)** needs US1's endpoint and page (T013, T018). T022/T023 (matcher) and T024/T025 (processor) can be done before the endpoint changes; T026/T027 need T013; T028/T029 need T018.
- **US3 (Phase 5)** needs US1's endpoint and page; the rate limit (T032/T033) is independent of the Web messaging (T030/T031).
- **US4 (Phase 6)** needs US1 (service and page); T036 and T037 are independent.
- **US5 (Phase 7)** needs Phase 2 (origin on records and DTOs) and US1; it does not depend on US2-US4.
- **Phase 8** after the stories you intend to ship.

### Parallel opportunities

- Phase 2: T002, T003, T005, T007 together.
- US1: T009, T011, T014, T016, T017, T019 together (separate test files), then the implementation chain.
- After US1: US2, US3 (rate limit), and US5 (API half) can proceed in parallel, since they touch different files except `DraftUploadService.cs` and `DraftUploadPage.razor` (coordinate T027, T029, T031, T038).

## Implementation Strategy

- **MVP**: Phase 1, Phase 2, and US1 (T001-T021). That delivers upload, draft, review, include, and discard through the main-screen button, with all safety checks (type from bytes, limits, ownership) in place.
- **Increment 2**: US2 (vehicle preselected) and US3 (clear refusals, rate limit), the two P2 stories.
- **Increment 3**: US4 and US5 (P3), then the Polish phase.
- Commit per task or per small group with Conventional Commits (for example `feat(drafts): add upload endpoint`), open one pull request for the feature after T044 passes.
