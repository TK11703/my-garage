# Research: Upload a Maintenance Record

All technical-context items were resolvable from the existing code and the specs of features 007 and 010; there were no open clarifications. Each decision below records what was chosen and why.

## R1. Reuse the email pipeline with a synthetic submission

- **Decision**: An upload becomes one `email_submissions` row (origin `upload`) with its `email_attachments` rows and blobs, owned by the signed-in user, in status `queued`. The existing `SubmissionProcessor` claims it and creates drafts exactly as for an email with attachments.
- **Rationale**: The user asked for the file to go "through the normal draft mechanism as an email based draft", and the spec asks for maximum reuse (FR-007, FR-012, FR-020). The processor already handles reading, type re-check, manual-entry drafts for unreadable files, vehicle matching, notifications, retries, and failure outcomes. The submission also gives uploads the same history, expiry, content purge, and orphan-blob sweep for free.
- **Alternatives considered**: (a) Call the extractor synchronously in the request and create drafts directly: a second code path for retries, failures, and manual entry, and a request that can take over a minute (rejected). (b) Call the existing intake endpoint from the Web app: that endpoint is for the trusted workflow only and requires the app role; giving the Web app that role would blur the trusted-service boundary in the constitution (rejected). (c) A separate `uploads` table: duplicates the queue, claim, and purge logic (rejected).

## R2. A user endpoint, not the intake endpoint

- **Decision**: `POST /api/drafts/uploads`, multipart form, in the existing `/api/drafts` group, authorized by the default policy for signed-in users. Owner from claims only.
- **Rationale**: Constitution: user endpoints refuse app-only tokens and derive ownership from claims. Because the caller is the owner, the trusted-service sender-lookup exception is not needed and sender matching is skipped entirely.
- **Alternatives considered**: Putting the route under `/api/intake` (rejected: that group requires the app role and a different rate-limit partition).

## R3. One submission per upload request, one draft per file

- **Decision**: All accepted files of one request share one submission; the processor produces one draft per attachment.
- **Rationale**: This is how a multi-attachment email already behaves (spec 007 story 4), so a bad or unreadable file does not block the others and each draft is decided independently. History shows one item per upload request.
- **Alternatives considered**: One submission per file (rejected: more rows and acknowledgements for no behavioral gain).

## R4. Share the attachment-storing logic instead of copying it

- **Decision**: Extract the file-type detection, SHA-256, safe display name, and blob upload/cleanup parts of `IntakeService` into a helper in `EmailIntake/` used by both `IntakeService` and the new `DraftUploadService`. Email behavior is unchanged and guarded by the existing intake tests (SC-006).
- **Rationale**: `FileSignature.Detect`, `SafeFileName`, the `EmailAttachmentRecord` construction, and the upload-then-commit-then-cleanup sequence are exactly what an upload needs; copying them would let the two drift.
- **Alternatives considered**: Calling `IntakeService` with a fabricated `IntakeEmailRequest` (rejected: it does sender lookup and base64 decoding, and a synthetic sender would weaken the "never trust a supplied owner" rule).

## R5. Vehicle preselection

- **Decision**: `email_submissions.preselected_vehicle_id` (nullable, FK to `vehicles` with `ON DELETE SET NULL`). The endpoint first loads the vehicle by owner and id; a vehicle that is not the caller's answers 404 and stores nothing. The insert statement keeps the value only when `vehicles.owner_id` matches. The processor passes it to `VehicleMatcher`, which returns `VehicleMatch.Selected` for it when it is still among the owner's vehicles; extraction hints (VIN, make, model) never override an explicit choice. The review picker still lets the user change it.
- **Rationale**: Satisfies FR-010 and the "Import Record" request without a separate draft-creation path. A new match value lets the review page say why the vehicle is chosen.
- **Alternatives considered**: Reusing `only_vehicle` or `vin` (rejected: wrong explanation to the user); setting the vehicle on the draft after creation (rejected: the draft is created in the processor).

## R6. New match value requires altering a CHECK constraint

- **Decision**: Migration `0015` drops `maintenance_drafts_vehicle_match_check` and re-adds it with `'selected'` included, adds `email_submissions.origin` (`email` default, `upload`) and `preselected_vehicle_id`.
- **Rationale**: Forward-only migrations are the only schema path (constitution VII); defaults keep every existing row valid. Existing migration counts in `MigrationRunnerTests` and `SqlScriptStoreTests` are bumped.
- **Alternatives considered**: Storing the origin only in the message id prefix (rejected: fragile and not queryable).

## R7. Records keep the from-email mark

- **Decision**: No change to the six record tables or their DTOs. Accepting an uploaded draft sets `from_email` exactly as today.
- **Rationale**: The user asked for the draft to be email-based, and the spec was amended accordingly (FR-022). The upload distinction lives on the draft and the history only.
- **Alternatives considered**: A new `from_upload` flag or a source enum on six tables (rejected: a cross-feature schema and UI change the user did not ask for). The "From email" badge wording on uploaded records is a known simplification, recorded here so it can be revisited.

## R8. Limits and rate limit

- **Decision**: Same limits as email attachments, shared constants: 5 files, 10 MB per file, 15 MB per request, type from file signature. A request over the body limit is refused with 413. A new per-user rate-limit policy `draft-upload` (`DraftUploads:RequestsPerMinute`, default 10) mirrors the sticker-import policy and reuses its 429 writer; tests set it high.
- **Rationale**: Spec FR-005 requires limits no looser than email; a per-user window (not per-address) fits a signed-in endpoint and bounds paid extraction calls.
- **Alternatives considered**: No rate limit (rejected: each file triggers a paid model call).

## R9. Per-file outcomes without echoing file names

- **Decision**: The response is `202 Accepted` with `DraftUploadResultDto { SubmissionId, AcceptedCount, Refused[] }`, where each refused entry has the file's index in the request and a reason code (`unsupported_type`, `empty`, `too_large`). If no file is accepted the answer is a 400 validation problem with the same per-file messages. The Web page maps indexes back to the names it sent and renders them escaped.
- **Rationale**: Meets US3 and US4 (name each bad file, queue the good ones) with no user-controlled text in API responses or logs (FR-024).
- **Alternatives considered**: Returning file names from the API (rejected: needless user-controlled text in responses).

## R10. Web: a page with a named SSR form, not a redirecting proxy

- **Decision**: `/drafts/upload` (optional `vehicleId` query value) with a named form handled in the page, following the sticker-import pattern: a handler reads `Request.Form.Files`, enforces the limits before streaming to the API, and renders per-file results inline. On success it redirects to `/drafts?uploaded=N`, which shows a status notice. The vehicle name is shown when a vehicle is preselected; a vehicle the API calls not found shows the standard not-found message.
- **Rationale**: The app is static SSR (no interactive circuit), the proxy-and-redirect pattern used for pictures can only carry fixed codes in the address, and this page must show several named results. The sticker pattern already solves multipart form handling and antiforgery in this app.
- **Alternatives considered**: A redirecting POST endpoint like picture upload (rejected: cannot show per-file results safely); interactive Blazor `InputFile` (rejected: introduces an interactive render mode the app does not use).

## R11. Where the buttons go

- **Decision**: "Add Maintenance" in the home page hero button row (always shown, since the vehicle can be chosen in review); "Import Record" above the accordion in `MaintenanceView`, linking to `/drafts/upload?vehicleId={id}`. Both are plain links with accessible names, per `docs/ui-standard.md`.
- **Rationale**: Matches the request; `MaintenanceView` is the single component behind the vehicle's maintenance tab, so one change covers it.

## R12. Reading is asynchronous; the user is told so

- **Decision**: After a successful upload the user lands on the awaiting-review list with a notice that the file is being read and the draft will appear there and in notifications. No polling (static SSR).
- **Rationale**: Reuses the existing queue and notification; spec assumption allows it. A draft usually appears within a minute.
- **Alternatives considered**: Waiting on the page until the draft exists (rejected: needs interactivity or long-held requests).
