# Feature Specification: Upload a Maintenance Record

**Feature Branch**: `011-upload-maintenance-record`

**Created**: 2026-11-05

**Status**: Draft

**Input**: User description: "I want to be able to upload a maintenance record, in addition to the capability already available in the logic app. Maybe the service record is available to me immediately as a file and not via email. I'd like to reuse as much as possible."

**Refinement (2026-11-05, planning input)**: The main screen gets an "Add Maintenance" button that simply uploads a file, which is then processed through the normal draft mechanism as an email-based draft. When a vehicle is displayed with its maintenance tab visible, an "Import Record" button does the same for that vehicle, with the vehicle already selected in the draft. A record created from an uploaded draft therefore carries the same source mark as one created from an email; an upload is only distinguishable in the draft list, the draft review, and the history.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Upload a record file and review what was found (Priority: P1)

A signed-in user has a maintenance document on hand, such as a service invoice PDF or a photo of a receipt, and it did not arrive by email. They choose the file in My Garage and send it. My Garage reads it for record details exactly as it does for an email attachment and queues the result as a draft maintenance item in the user's own review list. The user then reviews it with the same screens used for email drafts: they see the proposed record beside the original file, correct it, choose a vehicle, decide whether to keep the original file, and include or discard it. Nothing becomes a record until the user includes it.

**Why this priority**: This is the whole feature. It gives users a way to get a document they already hold into the same review-and-include flow without involving a mailbox.

**Independent Test**: Sign in, upload a service-invoice PDF, open the resulting draft, and confirm it shows the proposed service record (date, odometer, shop, line items, totals) beside the original file and that no record exists yet; include it on one of the user's vehicles and confirm the record exists on that vehicle and the draft leaves the review list.

**Acceptance Scenarios**:

1. **Given** a signed-in user, **When** they upload a PDF or image file that holds record details, **Then** a draft maintenance item appears in their review list, they are notified in the application as for any new draft, and no vehicle or record is created or changed.
2. **Given** an uploaded draft proposed as a service record, **When** the user opens it, **Then** they see the proposed details, line items and parts, the compatibility indication of what was found and what is missing, and the original file, as for an email draft.
3. **Given** an uploaded draft, **When** the user changes the record type, chooses a vehicle they own, corrects details, and includes it, **Then** the record is created on that vehicle with the same validation as a manually entered record and the draft leaves the review list.
4. **Given** an uploaded draft, **When** the user includes it and chooses to keep the original file, **Then** the file is stored privately with the new record in the same way as a kept email attachment; if they choose not to keep it, the file is removed.
5. **Given** an uploaded draft, **When** the user discards it, **Then** no record is created and the draft and its file are removed.
6. **Given** two users, **When** one uploads a file, **Then** the draft and its file are visible to and actionable by only the uploader.

---

### User Story 2 - Import a record from a vehicle's maintenance tab with the vehicle preselected (Priority: P2)

A user who is already looking at one of their vehicles can choose "Import Record" in that vehicle's maintenance tab. The draft that results is proposed for that vehicle from the start, so the user does not have to choose it during review. Uploading with "Add Maintenance" on the main screen leaves vehicle choice to the existing suggestion and review steps.

**Why this priority**: It saves a step and removes a source of mistakes, but the upload-and-review flow in Story 1 works without it.

**Independent Test**: Use "Import Record" in a vehicle's maintenance tab and confirm the draft is proposed for that vehicle; use "Add Maintenance" on the main screen with the same kind of file and confirm the vehicle is suggested or left for the user to choose, as for email drafts.

**Acceptance Scenarios**:

1. **Given** a user viewing one of their vehicles with the maintenance tab visible, **When** they choose "Import Record" and upload a file, **Then** the resulting draft is proposed for that vehicle and the user can still change it to another vehicle they own.
2. **Given** a user uploading with "Add Maintenance" on the main screen, **When** the file is read, **Then** the vehicle is suggested or left unchosen using the same logic as for email drafts.
3. **Given** a vehicle identifier that does not belong to the signed-in user, **When** an upload is attempted for it, **Then** the upload is refused as not found and nothing is stored.

---

### User Story 3 - Handle files that cannot be used (Priority: P2)

Users sometimes pick the wrong file, an oversized scan, a damaged file, or a file that is not a document at all. My Garage tells them clearly and immediately what is wrong and stores nothing. A valid file from which nothing can be read still becomes a draft marked for manual entry, so the user can type the details while looking at the original file or discard it.

**Why this priority**: Clear handling of bad input keeps the review list clean and avoids stored junk, but the main flow does not depend on it.

**Independent Test**: Upload an unsupported file type, an oversized file, a file whose name says PDF but whose content is something else, and an empty file; confirm each is refused with a reason and nothing is stored. Upload a valid but unreadable file and confirm it becomes a draft marked for manual entry.

**Acceptance Scenarios**:

1. **Given** a file of an unsupported type, over the size limit, empty, damaged, or whose actual content does not match its stated type, **When** the user uploads it, **Then** it is refused with a message naming the problem and nothing is stored.
2. **Given** no file selected, **When** the user submits the upload, **Then** they are told to choose a file and nothing is stored.
3. **Given** a valid file from which no details can be read, **When** it is uploaded, **Then** a draft marked for manual entry is queued with the original file available to view.
4. **Given** a temporary failure while reading or storing the file, **When** the user uploads, **Then** they are told it could not be completed and can retry, and no partial draft or stored file remains.

---

### User Story 4 - Upload several files at once (Priority: P3)

A user with a stack of invoices can select several files in one upload. Each file is handled separately and yields its own draft, so a bad file does not block the others, and each draft can be included or discarded independently.

**Why this priority**: A convenience for catching up on history; single-file upload already delivers the value.

**Independent Test**: Upload three files, one of them unsupported; confirm two drafts are queued, the unsupported file is reported by name, and each draft can be handled independently.

**Acceptance Scenarios**:

1. **Given** several valid files selected together, **When** the user uploads them, **Then** one draft per file is queued and the user is told how many drafts were created.
2. **Given** a mix of valid and invalid files, **When** the user uploads them, **Then** the valid ones are queued, each invalid one is reported with its reason, and nothing is stored for the invalid ones.
3. **Given** more files than the allowed number for one upload, **When** the user submits, **Then** the upload is refused with the limit stated and nothing is stored.

---

### User Story 5 - See where a draft came from (Priority: P3)

In the review list, on the review page, and in the history of items, an uploaded draft is clearly labelled as an upload with its file name and upload time, rather than an email sender and subject, so the user can tell uploaded drafts from emailed ones and see what became of each.

**Why this priority**: Supports trust and clarity but is not needed for the core flow.

**Independent Test**: Create one emailed draft and one uploaded draft and confirm each is labelled with its true source, and that an included uploaded draft's history entry links to its record.

**Acceptance Scenarios**:

1. **Given** drafts from both sources, **When** the user views the review list, **Then** each shows its source, and an uploaded draft shows its file name and upload time instead of a sender and subject.
2. **Given** an uploaded draft that was included, discarded, or expired, **When** the user views their history, **Then** it appears with its outcome and, if included, a link to the record.
3. **Given** a record created from an uploaded draft, **When** the user views it, **Then** it carries the same source mark as a record created from an email draft, distinguishing it from a manually entered record.

---

### Edge Cases

- The same file is uploaded twice; two drafts result, and the user is warned on the second that it looks like a duplicate, using the same duplicate indication already used for email drafts.
- A file's content contains markup, scripts, or text intended to mislead the reader or manipulate the reading step; it is untrusted, never executed, never displayed unescaped, and cannot create a record without the user's approval.
- A multi-page PDF or very large image is near the size limit.
- The user has no vehicles; the draft is queued without a vehicle and the user must add or choose a vehicle before including it.
- The user leaves the upload page while a file is being sent or read; no partial draft or stored file remains, or the completed draft appears in the review list.
- The user never reviews an uploaded draft; it expires after the same period as an email draft and is removed with its file.
- The reading service is unavailable or not configured; the user is told details could not be read and the file is still queued for manual entry, or the upload fails clearly and is not stored.
- Many uploads are made in a short burst by one user; excess uploads are refused with a retry message.
- A user is removed after uploading drafts that are still waiting.

## Requirements *(mandatory)*

### Functional Requirements

**Upload**

- **FR-001**: The system MUST let a signed-in user upload one or more PDF or image files from the web application to create draft maintenance items for their own review.
- **FR-002**: The system MUST offer an "Add Maintenance" upload on the main screen and an "Import Record" upload in a vehicle's maintenance tab.
- **FR-003**: The system MUST require the user to be signed in to upload and MUST attribute every upload to the signed-in user, taking identity only from the sign-in and never from anything supplied with the upload.
- **FR-004**: The system MUST identify the file type from the file's actual content rather than its name or stated type, and MUST refuse unsupported types, empty files, damaged files, and files over the size limit with a message naming the problem, storing nothing for them.
- **FR-005**: The system MUST limit the size of each file, the number of files per upload, and the rate of uploads per user, using limits no looser than those for email attachments, and MUST refuse excess uploads with a clear message.
- **FR-006**: The system MUST treat file content as untrusted and MUST NOT execute it or display it unescaped.

**Reading and drafts (reuse of the email draft flow)**

- **FR-007**: The system MUST read each accepted file for record details with the same reading, kind recognition, vehicle suggestion, and service-record line-item and parts extraction used for email attachments, so that an uploaded file and an identical emailed attachment yield equivalent drafts.
- **FR-008**: The system MUST queue the result as a draft maintenance item in the uploader's review list and notify the uploader in the application as for any new draft; one file yields one draft.
- **FR-009**: When nothing can be read from a valid file, the system MUST still queue a draft marked for manual entry with the original file available to view.
- **FR-010**: When an upload is started with "Import Record" from a vehicle's maintenance tab, the system MUST propose that vehicle for the draft after verifying the signed-in user owns it; a vehicle not owned by the user MUST be treated as not found.
- **FR-011**: The system MUST NOT create or change any vehicle or record from an upload without the user's explicit approval; every uploaded draft MUST wait in the user's review list until included or discarded.
- **FR-012**: Uploaded drafts MUST use the existing review experience unchanged: compatibility indication, changing the record type, choosing a vehicle, correcting details, choosing whether to keep the original file, including, and discarding, with the same validation as manually entered records.
- **FR-013**: Including an uploaded draft MUST create a new record of one of the kinds My Garage already keeps, marked as coming from an upload; it MUST only add records and never overwrite or delete existing ones.
- **FR-014**: When including an uploaded draft, a kept original MUST be stored privately and linked to the created record exactly as for kept email attachments, and an original not kept MUST be removed. Uploaded files MUST NOT appear among a vehicle's uploaded images.
- **FR-015**: Discarding an uploaded draft MUST create no record and MUST remove the draft's details and file.
- **FR-016**: An uploaded draft left unreviewed MUST expire and be removed with its file after the same period as an email draft.

**Access and integrity**

- **FR-017**: Uploaded drafts and their files MUST be visible to and actionable by only the uploader; every operation by identifier MUST verify ownership and otherwise behave as not found.
- **FR-018**: Files MUST be stored privately, MUST NOT be reachable by public address, and MUST be readable only by the owning user through the application.
- **FR-019**: Uploading MUST be all-or-nothing per file: a failure MUST NOT leave a draft without its file, a file without a draft, or a partial draft.
- **FR-020**: Uploading MUST NOT weaken or change the existing email intake: the trusted workflow's intake, sender matching, duplicate handling, and its history continue to behave as before.

**Source and history**

- **FR-021**: Every draft MUST carry its source, either email or upload. An uploaded draft MUST show its file name and upload time in place of an email sender and subject wherever drafts and history are shown.
- **FR-022**: Records created from an uploaded draft MUST carry the same source mark as records created from an email draft, distinguishable from manual entries.
- **FR-023**: The system MUST show the user the history of their uploads and emails together, each with its outcome (awaiting review, included, discarded, expired, not applicable, or failed) and a link to any record created.
- **FR-024**: The system MUST NOT write file names, file contents, extracted values, or credentials to logs.
- **FR-025**: The upload MUST follow the existing UI standard and be usable with keyboard and assistive technology, including clear progress, result, and error messages.

### Key Entities *(include if feature involves data)*

- **Upload**: One file a user hands to My Garage to be read for record details. Holds the file name, type, size, upload time, the uploading user, an optional vehicle it was started from, and its outcome. Plays, for uploaded files, the role the email submission plays for emails.
- **Draft Maintenance Item**: Existing entity. A candidate record proposed from an email attachment or text, now also from an uploaded file; gains a source (email or upload).
- **Stored Original File**: The uploaded file held privately and separately from vehicle images until the draft is decided; existing concept for email attachments, reused.
- **Kept Document**: Existing entity. An original file the user chose to keep with an included record, linked to that record.
- **Source Mark**: The existing from-email mark on a record; unchanged, and also set for records created from uploaded drafts. A draft additionally knows whether it began as an email or an upload.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A signed-in user can go from choosing a file to seeing the proposed record in review in under 1 minute for a typical single-page invoice, and can include or discard it in a further minute.
- **SC-002**: Zero vehicles or records are created or changed from an upload without the user's explicit approval.
- **SC-003**: 100% of uploaded drafts and files are visible only to the uploader; zero cross-user access in testing.
- **SC-004**: 100% of unsupported, oversized, empty, damaged, or mislabelled files are refused with a reason and result in nothing stored.
- **SC-005**: An uploaded file and an identical emailed attachment produce drafts with the same proposed kind and details in at least 95% of tested documents, showing the reading is shared rather than reimplemented.
- **SC-006**: Every existing email-intake behavior continues to pass its existing tests unchanged after this feature.
- **SC-007**: No uploaded file ever appears among a vehicle's uploaded images, and none is reachable without the owner's sign-in.
- **SC-008**: In a multi-file upload, 100% of valid files yield exactly one draft each regardless of any invalid files in the same upload.

## Assumptions

- Uploads are made by signed-in end users through the web application; there is no change to how the trusted workflow authenticates or submits, and no new machine-to-machine entry point.
- The review, notification, expiry, keep-original, and duplicate-indication behavior built for email drafts is reused as is; this feature adds a new way to create drafts and a source label, not a second review experience.
- Accepted file types, content-based type detection, and size limits match those for email attachments (PDF and common image files); the exact numbers, including files per upload and uploads per minute, are set in the plan and are no looser than the email limits.
- Uploaded drafts wait the same 30 days for review as email drafts and are then removed with their files.
- The reading step may take longer than the upload itself; if it is not finished when the upload completes, the draft appears in the review list with the usual notification once ready, and the user is told this.
- A user can upload only to their own vehicles; a vehicle chosen with "Import Record" is a proposal the user can change in review.
- Existing validation is the single source of truth for what is a valid record; an uploaded draft can only become a record through those rules.
- Pasting text, importing other formats (spreadsheets, scans of multiple documents in one file), and bulk back-fill tools are out of scope.
- Uploads are not deduplicated by content; a likely duplicate is indicated during review using the existing duplicate indication.
