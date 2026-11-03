# Data Model: Email Ingestion Intake

One forward-only migration extends the schema (applied by the existing migration runner with the checksum ledger). Applied scripts `0001`-`0011` are not edited. Blob containers are created by hosted services at startup.

## Migration 0012: `0012_create_email_intake.sql`

### `user_profiles`

Known users, so a sender address can be matched. Written only by the Api from validated claims.

| Column | Type | Rules |
|---|---|---|
| `owner_id` | `text` | Primary key; the token's object id (same value as `vehicles.owner_id`). |
| `email` | `varchar(320)` | `NULL` when the token carries no email. |
| `display_name` | `varchar(200)` | `NOT NULL`. |
| `first_seen_at`, `last_seen_at` | `timestamptz` | `NOT NULL`. |

Index: `ix_user_profiles_email (lower(email))` (not unique, so an ambiguous address is detectable and refused).

### `email_submissions`

One row per message id. The work queue and the duplicate guard.

| Column | Type | Rules |
|---|---|---|
| `id` | `uuid` | Primary key (UUIDv7). |
| `message_id` | `varchar(512)` | `NOT NULL`, unique. |
| `status` | `varchar(14)` | `NOT NULL`, check `IN ('queued','processing','ready','not_applicable','not_accepted','failed')`. |
| `arrived_at` | `timestamptz` | `NOT NULL`; server clock at receipt. |
| `received_at` | `timestamptz` | `NULL` for `not_accepted`; the mailbox time sent by the workflow. |
| `owner_id` | `text` | `NULL` for `not_accepted`; the matched user. |
| `sender` | `varchar(320)` | `NULL`; cleared when content is purged. |
| `subject` | `varchar(500)` | `NULL`; cleared when content is purged. |
| `body_text` | `text` | `NULL`; at most 100,000 characters; cleared when content is purged. |
| `attempt_count` | `smallint` | `NOT NULL DEFAULT 0`. |
| `next_attempt_at` | `timestamptz` | `NOT NULL`; the queue ordering key. |
| `locked_until` | `timestamptz` | `NULL`; lease while `processing`. |
| `failure_reason` | `varchar(200)` | `NULL`; a short code, never email content. |
| `skipped_attachments` | `jsonb` | `NOT NULL DEFAULT '[]'`; array of `{ "fileName", "reason" }` for attachments not stored (`unsupported_type`, `damaged`); reasons are codes, file names are display only; cleared when content is purged. |
| `content_purged_at` | `timestamptz` | `NULL`. |

Indexes: `ix_email_submissions_queue (next_attempt_at) WHERE status IN ('queued','processing')`; `ix_email_submissions_owner (owner_id, arrived_at DESC, id DESC) WHERE owner_id IS NOT NULL`.

State transitions: `queued` -> `processing` -> `ready` (one or more drafts) | `not_applicable` | back to `queued` (retry, with `attempt_count` and `next_attempt_at`) | `failed`. A `failed` row is re-queued when the same message id arrives again. `not_accepted` is final and is created directly by the intake endpoint.

### `email_attachments`

| Column | Type | Rules |
|---|---|---|
| `id` | `uuid` | Primary key. |
| `submission_id` | `uuid` | `NOT NULL`, references `email_submissions (id)` `ON DELETE CASCADE`. |
| `owner_id` | `text` | `NOT NULL`; ownership for the blob is proved here, not through a vehicle. |
| `file_name` | `varchar(255)` | `NOT NULL`; display only. |
| `content_type` | `varchar(20)` | `NOT NULL`, check `IN ('application/pdf','image/jpeg','image/png','image/webp')`; detected from bytes. |
| `size_bytes` | `integer` | `NOT NULL`, check `BETWEEN 1 AND 10485760`. |
| `sha256` | `char(64)` | `NOT NULL`; lower-case hex, used for duplicate warnings. |
| `created_at` | `timestamptz` | `NOT NULL`. |

Blob: container `email-intake`, name `{submission_id}/{id}`. Index: `ix_email_attachments_owner_hash (owner_id, sha256)`.

### `maintenance_drafts`

A proposed record waiting for the user (the spec's "review item").

| Column | Type | Rules |
|---|---|---|
| `id` | `uuid` | Primary key. |
| `owner_id` | `text` | `NOT NULL`; every statement filters on it. |
| `submission_id` | `uuid` | `NOT NULL`, references `email_submissions (id)` `ON DELETE CASCADE`. |
| `attachment_id` | `uuid` | `NULL` (text source); references `email_attachments (id)` `ON DELETE SET NULL`. |
| `state` | `varchar(12)` | `NOT NULL`, check `IN ('needs_review','manual_entry','included','discarded','expired')`. |
| `kind` | `varchar(14)` | `NULL` only while `manual_entry`; check `IN ('service_record','fuel','energy','inspection','registration','insurance','tax')`. |
| `vehicle_id` | `uuid` | `NULL`; references `vehicles (id)` `ON DELETE SET NULL`. |
| `vehicle_match` | `varchar(12)` | `NOT NULL`, check `IN ('vin','description','only_vehicle','none')`. |
| `confidence` | `numeric(3,2)` | `NULL`, check `BETWEEN 0 AND 1`. |
| `proposal` | `jsonb` | `NOT NULL DEFAULT '{}'`; serialized `DraftProposal`; cleared (`'{}'`) when decided or expired. |
| `notes` | `varchar(500)` | `NULL`; the model's short remark, treated as untrusted text. |
| `created_record_kind`, `created_record_id` | `varchar(14)`, `uuid` | Set when `included`; traces the record to its email. |
| `created_at` | `timestamptz` | `NOT NULL`. |
| `expires_at` | `timestamptz` | `NOT NULL`; `created_at + 30 days`. |
| `decided_at` | `timestamptz` | `NULL`. |

Indexes: `ix_maintenance_drafts_owner_open (owner_id, created_at DESC, id DESC) WHERE state IN ('needs_review','manual_entry')`; `ix_maintenance_drafts_expiry (expires_at) WHERE state IN ('needs_review','manual_entry')`; `ix_maintenance_drafts_submission (submission_id)`.

State transitions: `needs_review` | `manual_entry` -> `included` (accept) | `discarded` (discard) | `expired` (30 days). `manual_entry` -> `needs_review` when the user chooses a kind. Decided states are final.

### `notifications`

| Column | Type | Rules |
|---|---|---|
| `id` | `uuid` | Primary key. |
| `owner_id` | `text` | `NOT NULL`. |
| `kind` | `varchar(20)` | `NOT NULL`, check `IN ('draft_ready')` (extensible by new migrations). |
| `draft_id` | `uuid` | `NULL`; references `maintenance_drafts (id)` `ON DELETE CASCADE`. |
| `title` | `varchar(120)` | `NOT NULL`; generic text, no sender or subject. |
| `created_at` | `timestamptz` | `NOT NULL`. |
| `read_at` | `timestamptz` | `NULL`. |

Indexes: `ix_notifications_owner (owner_id, created_at DESC, id DESC)`; `ix_notifications_unread (owner_id) WHERE read_at IS NULL`.

### `vehicle_documents`

Originals the user chose to keep. Not a vehicle image.

| Column | Type | Rules |
|---|---|---|
| `id` | `uuid` | Primary key. |
| `vehicle_id` | `uuid` | `NOT NULL`, references `vehicles (id)` `ON DELETE CASCADE`; ownership is proved through the vehicle (as in features 005 and 006). |
| `file_name` | `varchar(255)` | `NOT NULL`. |
| `content_type` | `varchar(20)` | `NOT NULL`, same check list as attachments. |
| `size_bytes` | `integer` | `NOT NULL`, check `BETWEEN 1 AND 10485760`. |
| `source_kind` | `varchar(14)` | `NULL`; the record kind it was kept with. |
| `source_record_id` | `uuid` | `NULL`; informational link, no foreign key (research Complexity Tracking). |
| `created_at` | `timestamptz` | `NOT NULL`. |
| `created_by` | `text` | `NOT NULL`; the authenticated owner id. |

Blob: container `vehicle-documents`, name `{vehicle_id}/{id}`. Index: `ix_vehicle_documents_vehicle (vehicle_id, created_at DESC, id DESC)`.

### Changes to existing tables

`ALTER TABLE` adds `from_email boolean NOT NULL DEFAULT false` to `service_records`, `fuel_energy_entries`, `inspections`, `registrations`, `insurance_policies`, and `taxes`. Set only by the accept transaction. Existing rows and existing code paths are unaffected (default `false`).

## Shared DTOs (`MyGarage.Shared`, not persisted as such)

| DTO | Fields |
|---|---|
| `IntakeEmailRequest` | `MessageId`, `ReceivedAt`, `From`, `Subject`, `BodyText`, `Attachments[]` |
| `IntakeAttachmentRequest` | `FileName`, `ContentType`, `IsInline`, `ContentBase64` |
| `IntakeAcknowledgement` | `SubmissionId`, `Status` (always `received`) |
| `DraftProposal` | `Kind`, plus one populated section: `ServiceRecord` (`DateServiced`, `OdometerReading`, `ShopOrPerson`, `LineItems[]` reusing `ServiceLineItemRequest`/`ServicePartRequest`), `FuelEnergy`, `Inspection`, `Registration`, `Insurance`, `Tax` (each shaped like the matching existing request DTO) |
| `MaintenanceDraftDto` | `Id`, `State`, `Kind`, `Proposal`, `Vehicle` (`VehicleId`, `Match`, `Candidates[]`), `Source` (`Text`/attachment name, content type, size), `Email` (arrival time, sender, subject while retained), `Compatibility`, `Confidence`, `Notes`, `ExpiresAt`, `HasAttachment` |
| `MaintenanceDraftSummaryDto` | `Id`, `State`, `Kind`, `VehicleName`, `ArrivedAt`, `ExpiresAt`, `HeadlineTotal` |
| `CompatibilityDto` | `Kind`, `IsAcceptable`, `Fields[]` (`Path`, `Status` = `ok | missing | invalid`, `Message`), `Warnings[]` |
| `AcceptDraftRequest` | `VehicleId`, `Proposal`, `KeepOriginal` |
| `ChangeDraftKindRequest` | `Kind` |
| `EmailHistoryItemDto` | `SubmissionId`, `ArrivedAt`, `Outcome` (`awaiting_review | included | discarded | expired | not_applicable | failed`), `Records[]` (kind, vehicle id, record id) |
| `NotificationDto` | `Id`, `Kind`, `Title`, `DraftId`, `CreatedAt`, `IsRead` |
| `UnreadCountDto` | `Unread`, `DraftsAwaitingReview` |
| `VehicleDocumentDto` | `Id`, `FileName`, `ContentType`, `SizeBytes`, `SourceKind`, `SourceRecordId`, `CreatedAt` |

Enums owned by this feature (`DraftKind`, `DraftState`, `VehicleMatch`, the compatibility status, the history outcome) travel as lowercase snake_case strings through `SnakeCaseEnumConverter` and are stored as the same lowercase text, with explicit mapper conversion, as in feature 006. The enums inside the proposal sections reuse the existing feature 005/006 enums and keep their existing JSON names, which are PascalCase: `FuelEnergyKind` (`Fuel`, `Energy`), `InspectionType` (`Safety`, `Emissions`), `InsuranceDuration` (`Monthly`, `BiAnnual`, `Annual`), `TaxType` (`PersonalProperty`, `Lease`, `Purchase`), `TaxRecurrence` (`Once`, `Monthly`, `BiAnnual`, `Annual`). The stored proposal JSON uses the same names.

## Validation rules (summary)

- **Intake**: `messageId` 1-512 characters; `from` a valid address up to 320 characters; `receivedAt` not more than 5 minutes in the future; `subject` up to 500; `bodyText` up to 100,000; at most 5 attachments (inline ones ignored and not counted against the limit but still size-checked); each valid base64 of 1 byte to 10 MB; 15 MB total decoded. An attachment that is valid base64 within the limits but whose bytes are not a PDF, JPEG, PNG, or WebP signature is skipped (recorded in `skipped_attachments`) and the rest of the submission is still processed; invalid base64 or an over-limit attachment refuses the whole request.
- **Accept**: `vehicleId` must be one of the caller's vehicles; the proposal's kind section is validated by the existing request validator of that kind (feature 005 and 006 rules, including the dates-not-in-the-future rules); a service record also requires `OdometerReading`.
- **Change kind**: any of the seven kinds; draft must be undecided.
