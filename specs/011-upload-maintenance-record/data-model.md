# Data Model: Upload a Maintenance Record

No new tables. Uploads reuse `email_submissions`, `email_attachments`, `maintenance_drafts`, `notifications`, and `vehicle_documents` from migration `0012`.

## Migration `0015_add_upload_origin.sql` (forward-only)

| Change | Detail |
|---|---|
| `email_submissions.origin` | `varchar(6) NOT NULL DEFAULT 'email' CHECK (origin IN ('email', 'upload'))`. Existing rows read `email`. |
| `email_submissions.preselected_vehicle_id` | `uuid NULL REFERENCES vehicles (id) ON DELETE SET NULL`. Set only for an upload started with "Import Record"; always null for email. |
| `maintenance_drafts.vehicle_match` | Drop `maintenance_drafts_vehicle_match_check`, re-add allowing `'vin', 'description', 'only_vehicle', 'selected', 'none'`. |

No index changes: the queue index and owner history index already cover the new rows.

## Entities

### Upload (an `email_submissions` row with `origin = 'upload'`)

| Field | Value for an upload |
|---|---|
| `id` | New version-7 guid. |
| `message_id` | `upload:{id}`; unique like any message id, so it can never collide with a real message id from the workflow. |
| `status` | `queued`, then `ready`, `not_applicable`, or `failed` through the existing processor. |
| `arrived_at`, `received_at` | Upload time (`TimeProvider`). |
| `owner_id` | The signed-in owner, from claims only. |
| `sender`, `subject`, `body_text` | Always null. |
| `origin` | `upload`. |
| `preselected_vehicle_id` | The owner's vehicle chosen with "Import Record", else null. |
| `skipped_attachments` | `[]`; refused files are never stored, so nothing is recorded for them. |

### Uploaded file (an `email_attachments` row, unchanged)

`file_name` (display only, sanitized as for email), `content_type` (from file signature: PDF, JPEG, PNG, WebP), `size_bytes` (1 to 10,485,760), `sha256`, `owner_id`. The blob lives in the `email-intake` container at `{submissionId}/{attachmentId}`, private, readable only through the owner-checked draft attachment endpoint.

### Draft (a `maintenance_drafts` row, unchanged)

One per stored file, created by the existing processor. `vehicle_id` is the preselected vehicle with `vehicle_match = 'selected'` when one was chosen; otherwise the normal VIN, description, only-vehicle, or none result. `state`, `kind`, `proposal`, `expires_at` (30 days), `created_record_*` behave as today.

## State transitions

Unchanged. Submission: `queued` -> `processing` -> `ready` | `not_applicable` | `failed` (with retry). Draft: `needs_review` | `manual_entry` -> `included` | `discarded` | `expired`. An upload whose every file cannot be read still yields `manual_entry` drafts; an upload with no storable file never creates a submission (the request is refused).

## Validation rules

| Rule | Where |
|---|---|
| 1 to 5 files per request; each 1 byte to 10 MB; total at most 15 MB | `DraftUploadRequestValidator` (count and total); per-file size and type in `DraftUploadService` |
| Type from leading bytes: PDF, JPEG, PNG, WebP; else refused `unsupported_type` | shared `FileSignature` |
| Empty file refused `empty`; over 10 MB refused `too_large` | `DraftUploadService` |
| `vehicleId`, when present, must be the caller's vehicle, else 404 and nothing stored | `DraftUploadService` (owner-scoped lookup) |
| At least one file accepted, else 400 with per-file messages | `DraftUploadService` |
| Per-user rate limit (default 10 per minute) | rate-limit policy `draft-upload` |

## DTO changes (`MyGarage.Shared`)

| DTO | Change |
|---|---|
| `DraftOrigin` (new enum, JSON `email`, `upload`) | Used by the DTOs below. |
| `MaintenanceDraftDto`, `MaintenanceDraftSummaryDto` | `Origin` (defaults to `Email`). For an upload, `Email` carries only `ArrivedAt` (labelled "Uploaded" by the page) while the draft is open. |
| `EmailHistoryItemDto` | `Origin` (defaults to `Email`). |
| `VehicleMatch` | New member `Selected` (JSON `selected`). |
| `DraftUploadResultDto` (new) | `SubmissionId`, `AcceptedCount`, `Refused` (list of `{ Index, Reason }`). |

Records and their DTOs (`FromEmail`) are unchanged.
