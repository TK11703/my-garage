# Feature Specification: Email Ingestion Intake

**Feature Branch**: `007-email-ingestion-api`

**Created**: 2026-11-03

**Status**: Draft

**Input**: User description: "I'm going to have an external logic app monitor an email box. When a mail comes into this box, the contents of the email are going to be transformed and sent to my API for possible ingestion. I'll give more specifics in the plan. We are going to need to handle the API call from the external logic app, then process it accordingly."

**Refinement (2026-11-03)**: The external workflow sends the sender's email address with each submission. The address is looked up among the application's users and a submission is accepted for ingestion only when it matches. Because an address can be spoofed, nothing an email contains ever becomes a record on its own: the matched user must review each item and include or discard it. Attachments (PDF or image files) may hold the record data, so they are processed too; they are kept in their own private storage area, separate from vehicle images, and the user decides whether to keep the original file.

**Refinement (2026-11-03, planning input)**: A queued item is also called a draft maintenance item. The user is alerted to new drafts through the notifications already shown in the navigation bar. A draft proposes a match to one maintenance kind; for a service record it also proposes the line items (and parts) read from the PDF, and the user can see how well the proposal fits a service record (what was found, what is missing) before accepting it as is or editing it and saving.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Accept a transformed email from the external workflow (Priority: P1)

An external automated workflow watches a dedicated mailbox. Each time a message arrives, the workflow converts it into a structured submission (sender address, subject, text, and any attachments) and sends it to My Garage. My Garage verifies the call really comes from that trusted workflow, checks the submission is well formed, and confirms receipt so the workflow knows the message was handed over.

**Why this priority**: Without a trustworthy, well-defined intake point nothing else in this feature can work. It is the smallest slice that can be demonstrated on its own.

**Independent Test**: Send a well-formed submission as the trusted workflow and confirm it is acknowledged; send the same shape of submission without credentials, with the wrong credentials, and with missing required content, and confirm each is refused with a specific reason and nothing is stored.

**Acceptance Scenarios**:

1. **Given** the trusted workflow presents valid credentials, **When** it sends a well-formed submission, **Then** My Garage acknowledges receipt and keeps a record of the submission and its outcome.
2. **Given** a caller presents no credentials, invalid credentials, or credentials that are not those of the trusted workflow, **When** it sends a submission, **Then** the submission is refused, nothing is stored, and no information about users or vehicles is revealed.
3. **Given** the trusted workflow sends a submission with a required value missing or malformed (for example no sender or no message identifier) or over the allowed size, **When** My Garage checks it, **Then** the submission is refused with a message identifying each problem.
4. **Given** a user signed in through the normal user sign-in, **When** they attempt to send a submission, **Then** it is refused, because only the trusted workflow may submit.

---

### User Story 2 - Match the sender to a user and queue items for review (Priority: P1)

My Garage looks up the sender address among its users. If it matches exactly one user, the content of the email, including anything read from its attachments, is turned into one or more items that wait in that user's review list. Nothing is added to the user's vehicles or records at this stage. If the sender matches no user, the submission is not accepted for ingestion, nothing is kept, and the workflow receives the same acknowledgement it would for any other valid submission so it cannot be used to discover who has an account.

**Why this priority**: Matching and queuing is the purpose of the feature. Making every item wait for review is what protects users from spoofed email, so it is equally critical.

**Independent Test**: Submit an email from a known user's address and confirm one or more items appear in that user's review list and no vehicle record changed; submit an email from an unknown address and confirm nothing appears for any user and nothing is stored; submit with the address in different letter case and confirm it still matches.

**Acceptance Scenarios**:

1. **Given** a sender address that matches exactly one user, **When** the submission is processed, **Then** the items found in it are placed in that user's review list as awaiting review, and no vehicle or record is created or changed.
2. **Given** a sender address that matches no user, or more than one, **When** the submission is processed, **Then** no item is created for anyone, the email content and attachments are discarded, and the outcome is recorded as not accepted.
3. **Given** a submission whose content and attachments hold nothing My Garage can store, **When** it is processed, **Then** the matched user is not shown an empty item and the outcome is recorded as not applicable.
4. **Given** a submission that names a vehicle the matched user does not own, **When** it is processed, **Then** the item is queued without a vehicle and the user must choose one of their own vehicles during review.
5. **Given** processing fails part way, **When** the failure occurs, **Then** no partial items or stored files remain and the outcome records the failure so the submission can be retried.

---

### User Story 3 - Review, include, or discard queued items (Priority: P1)

A signed-in user sees how many items are waiting for review and can open their review list. Each item shows the email's sender, subject, and arrival time, the record details found, and a view of the original attachment when there is one. The user can correct the details, choose which of their vehicles it belongs to, and either include it, which creates the record, or discard it. When including an item that came with an attachment, the user chooses whether to keep the original file with that record; if they do, it is stored in their media area, kept apart from the vehicle's uploaded images.

**Why this priority**: This is the only way an email ever becomes a record, so it completes the value of the feature and is the user's protection against spoofed email.

**Independent Test**: With an item waiting, include it with a chosen vehicle and confirm the record exists on that vehicle, is marked as coming from an email, and the item leaves the review list; do the same keeping the original file and confirm the file is available in the media area and does not appear among the vehicle's images; discard another item and confirm no record was created and its attachment is gone; confirm a second user cannot see or act on either item.

**Acceptance Scenarios**:

1. **Given** a user with items waiting, **When** they sign in, **Then** they receive a notification about each new draft, can see how many items await review, and can reach the review list.
2. **Given** an item with details found, **When** the user chooses a vehicle they own and includes it, **Then** the record is created on that vehicle with the (possibly corrected) details, marked as coming from an email, and the item leaves the review list.
2a. **Given** a draft proposed as a service record, **When** the user opens it, **Then** they see the proposed service record (date, odometer, shop, line items with parts and totals) beside the original document, and a clear indication of which required service-record details were found and which are missing or doubtful.
2b. **Given** a draft whose proposed kind is wrong, **When** the user changes the record type, **Then** the details are re-presented as the chosen kind, carrying over what fits.
3. **Given** an item that came with an attachment, **When** the user includes it and chooses to keep the original file, **Then** the file is stored privately in their media area, linked to the new record, and is not listed with the vehicle's uploaded images.
4. **Given** an item that came with an attachment, **When** the user includes it without keeping the original file, **Then** the record is created and the file is removed.
5. **Given** an item in the review list, **When** the user discards it, **Then** no record is created and its details and any attachment are removed.
6. **Given** an item without a vehicle chosen, or with invalid details, **When** the user tries to include it, **Then** nothing is created and each problem is identified next to its field.
7. **Given** another user's item, **When** a user tries to view, include, discard, or open its attachment, **Then** it is treated as not found.
8. **Given** an item that has waited longer than the review period, **When** the period ends, **Then** it is discarded automatically together with any attachment.

---

### User Story 4 - Read data from PDF and image attachments (Priority: P2)

Many emails (receipts, invoices, registration or insurance notices) carry their data in an attached PDF or image rather than the text. My Garage accepts PDF and common image files, reads them for record details, and queues what it finds for review alongside the original file. Files of other types, damaged files, and files that are too large are not stored and are reported on the item. If nothing can be read, the item is still queued so the user can enter the details by hand from the original file or discard it.

**Why this priority**: It widens what can be ingested to the most common real-world case, but the text-only flow already delivers value without it.

**Independent Test**: Submit emails with a PDF, an image, an unsupported file, an oversized file, and a file whose name says PDF but whose content is something else; confirm the first two produce items showing the details found with the original file, an unreadable but valid file produces an item marked for manual entry, and the others are rejected with a reason and nothing stored for them.

**Acceptance Scenarios**:

1. **Given** an email with a PDF or image attachment holding record details, **When** it is processed, **Then** the details found are shown on the queued item and the original file is stored privately in an area separate from vehicle images.
2. **Given** an attachment of an unsupported type, or one over the size limit, or one whose actual content does not match its stated type, **When** it is processed, **Then** the file is not stored, the item reports why, and any other valid attachments and the text are still processed.
3. **Given** a valid attachment from which no details can be read, **When** it is processed, **Then** the item is queued marked for manual entry with the original file available to view.
4. **Given** an email with several attachments, **When** it is processed, **Then** each is handled separately and the user can include or discard each resulting item independently.

---

### User Story 5 - Handle repeated and retried submissions safely (Priority: P2)

The external workflow may retry a call after a timeout or send the same email twice. My Garage recognizes a message it has already handled and responds consistently, without queuing or storing anything a second time.

**Why this priority**: Automated mail workflows commonly retry; duplicates would clutter a user's review list and storage, but the feature works in a limited way without this.

**Independent Test**: Send the same submission twice and confirm one set of items and files exists and both calls receive a consistent acknowledgement that reports the earlier outcome.

**Acceptance Scenarios**:

1. **Given** a message identifier already handled successfully, **When** it is sent again, **Then** nothing further is queued or stored and the response reports the original outcome.
2. **Given** an earlier attempt failed during processing, **When** the same message is sent again, **Then** it is processed again rather than treated as a duplicate.
3. **Given** two identical submissions arrive at the same moment, **When** both are processed, **Then** items and files are created at most once.

---

### User Story 6 - See what happened to emails sent to you (Priority: P3)

A signed-in user can see a history of emails attributed to them with their arrival time and what became of each: awaiting review, included (with a link to the record), discarded, expired, or not applicable.

**Why this priority**: It supports trust and answers "what happened to that email?" but is not needed for the core flow.

**Independent Test**: Produce items ending in each state and confirm each appears with the right state, and that no user sees another user's history.

**Acceptance Scenarios**:

1. **Given** a user with items in different states, **When** they open the history, **Then** each is listed with its arrival time and state, and included items link to their record.
2. **Given** a signed-in user, **When** they open the history, **Then** they see only emails attributed to them.

---

### Edge Cases

- A message arrives with an empty body and only attachments, or only an inline image such as a signature logo.
- The same email is delivered to the mailbox twice with different message identifiers.
- A submission or attachment is very large, or contains markup, scripts, or content intended to mislead or harm; all of it is treated as untrusted and is never executed or displayed unescaped.
- The sender address differs only in case or formatting from a registered address; it still matches.
- The sender address is spoofed but matches a real user; the email is queued but never creates a record without that user's approval.
- A user has no vehicles, or several vehicles that could plausibly match the content.
- A user never reviews an item.
- A user discards or includes an item while it is still being processed or while a duplicate arrives.
- The external workflow sends many submissions in a short burst.
- The submission's received time is in the future or very old.
- The external workflow credentials are rotated or revoked while submissions are in flight.
- The data store or file storage is unavailable when a submission arrives; the workflow receives a clear retryable failure.
- A user is removed after items were queued for them.

## Requirements *(mandatory)*

### Functional Requirements

**Intake and security**

- **FR-001**: The system MUST provide an intake point that accepts one structured email submission per call from the external workflow.
- **FR-002**: The system MUST accept submissions only from the trusted external workflow, verified by its credentials, and MUST refuse all other callers, including signed-in end users, without revealing any user or vehicle information.
- **FR-003**: The system MUST validate every submission before any processing and refuse invalid submissions with a response that identifies each problem.
- **FR-004**: A submission MUST carry, at minimum, a unique message identifier, the sender address, the time the email was received, a subject, and the message text, plus zero or more attachments; the remaining fields are defined in the plan.
- **FR-005**: The system MUST limit the size of a submission, the number of attachments, and the size of each attachment, and refuse submissions that exceed the limits.
- **FR-006**: The system MUST treat all submission content and attachments as untrusted and MUST NOT execute them or display them unescaped.

**Sender matching**

- **FR-007**: The system MUST look up the sender address, compared case-insensitively, among the registered users and MUST accept the submission for ingestion only when it matches exactly one user.
- **FR-008**: When the sender matches no user or more than one, the system MUST NOT create items, MUST NOT keep the email content or attachments, MUST record only the message identifier, arrival time, and a "not accepted" outcome, and MUST give the workflow the same acknowledgement as for an accepted submission. The acknowledgement never discloses whether the sender matched a user.
- **FR-009**: The system MUST NEVER create items for a user other than the matched user, and MUST NEVER trust a user or owner identifier supplied in the submission as proof of ownership.

**Review before anything is stored as a record**

- **FR-010**: The system MUST NOT create or change any vehicle or record from an email without the matched user's explicit approval; every item MUST wait in that user's review list until the user includes or discards it.
- **FR-011**: The system MUST notify the matched user in the application when a new draft is ready, show how many of their items await review, and let them open the list.
- **FR-012**: Each item MUST show the sender, subject, arrival time, the details found, the source (text or a named attachment), and access to the original attachment when present. An item proposed as a service record MUST also show its line items and parts and a compatibility indication of which service-record details were found, missing, or doubtful.
- **FR-013**: The user MUST be able to correct an item's details and choose which of their own vehicles it belongs to before including it; an item MUST NOT be includable without an owned vehicle and valid details.
- **FR-014**: Including an item MUST create a new record of one of the kinds My Garage already keeps for a vehicle (service records, fuel and charging entries, inspections, registrations, insurance, and taxes), marked as coming from an email; ingestion MUST only add records and MUST NOT overwrite or delete existing ones. The rules for recognizing each kind are defined in the plan.
- **FR-015**: Discarding an item MUST create no record and MUST remove the item's details and any stored attachment.
- **FR-016**: Items MUST be visible to and actionable by only the matched user; every item and attachment operation by item identifier MUST verify ownership and otherwise behave as not found.
- **FR-017**: An item left unreviewed for 30 days MUST be discarded automatically together with its attachment.

**Attachments**

- **FR-018**: The system MUST accept PDF and common image files as attachments, identify the type from the file's actual content rather than its name or stated type, and refuse other types, damaged files, and files over the size limit without storing them, while still processing the rest of the submission.
- **FR-019**: The system MUST read accepted attachments for record details and place what it finds on the queued item; when nothing can be read, it MUST still queue the item marked for manual entry.
- **FR-020**: Accepted attachments MUST be stored privately in a storage area separate from vehicle images, MUST NOT be reachable by public address, and MUST be readable only by the matched user through the application.
- **FR-021**: When including an item with an attachment, the user MUST be able to choose whether to keep the original file; a kept file MUST be linked to the created record, available in the user's media area, and not listed among the vehicle's uploaded images. A file not kept MUST be removed.
- **FR-022**: Files stored for an item and the item's records MUST stay consistent: a failure MUST NOT leave an item without its file, a file without an item, or a kept file without its record.

**Reliability and records**

- **FR-023**: Processing a submission MUST be all-or-nothing; a failure MUST NOT leave partial items or files.
- **FR-024**: The system MUST recognize an already-handled message identifier and MUST NOT queue or store anything a second time, responding with the same acknowledgement as the first call. A message whose earlier attempt failed MUST be eligible for reprocessing.
- **FR-025**: The system MUST respond to the workflow in a way it can act on: success when received (including duplicates and unmatched senders), a client-error response for invalid or unauthorized submissions, and a retryable error response when the system cannot process because of its own temporary failure.
- **FR-026**: The system MUST give every submission exactly one recorded outcome: awaiting review, included, discarded, expired, not applicable, not accepted, or failed.
- **FR-027**: The system MUST show a signed-in user a history of the emails attributed to them with arrival time, outcome, and a link to any record created, and MUST show them nothing that belongs to another user.
- **FR-028**: Email text, extracted details, and unkept attachments MUST be removed when an item is included, discarded, or expires, keeping only the non-content details (message identifier, arrival time, outcome, and link to any created record) so duplicate detection and history continue to work.
- **FR-029**: The system MUST NOT write email content, sender addresses, attachment contents, or credentials to logs.

### Key Entities *(include if feature involves data)*

- **Email Submission**: One message handed over by the external workflow. Holds the unique message identifier, arrival time, matched user (when there is one), and outcome; the sender, subject, and text are held only while content is retained.
- **Review Item**: A candidate record found in an email's text or in one attachment. Holds the details found, the source, the suggested or chosen vehicle, the state (awaiting review, included, discarded, expired, marked for manual entry), and a link to its original attachment when there is one.
- **Email Attachment**: An original PDF or image file from an email, held privately and separately from vehicle images, owned by the matched user, with its name, type, and size.
- **Kept Document**: An original file the user chose to keep with an included record, available in the user's media area and linked to that record.
- **Ingested Record Link**: The relationship between a review item and the record created from it, so the record can be traced to its email.
- **Trusted Workflow Identity**: The identity under which the external workflow authenticates; distinct from end users.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of submissions from callers other than the trusted workflow are refused and result in nothing stored.
- **SC-002**: 100% of submissions whose sender matches no user result in no items, no stored content, and no stored files, and the workflow cannot tell them apart from accepted submissions.
- **SC-003**: Zero vehicles or records are created or changed from an email without the matched user's explicit approval.
- **SC-004**: 100% of queued items and attachments are visible only to the matched user; zero cross-user access in testing.
- **SC-005**: Sending the same message any number of times results in its items and files existing exactly once.
- **SC-006**: A user can review an item and include or discard it in under 1 minute.
- **SC-007**: The workflow receives an acknowledgement within 5 seconds for 95% of submissions, with or without attachments, under normal load; reading the content happens after the acknowledgement.
- **SC-008**: Every submission, whatever its result, has exactly one recorded outcome.
- **SC-009**: No file from an email ever appears among a vehicle's uploaded images, and no attachment is ever reachable without the owning user's sign-in.

## Assumptions

- The external workflow, the mailbox, and the transformation of an email into a submission are outside this application; this feature only receives and processes the result.
- Exactly one external workflow is trusted, identified by its own credentials, which are issued, rotated, and kept outside source control. The existing user sign-in is unchanged and the workflow is never a user.
- The workflow sends attachments as part of the submission; how they are packaged, and the size limits, are defined in the plan.
- User matching uses the email address stored on the user's account; a user cannot add extra sender addresses in this feature.
- Spoofed senders are handled by the review step rather than by verifying the email's origin, which is the workflow's concern.
- Items wait 30 days for review before expiring, and the user's review list is a new area of the existing web application. Notification is in the application only (the notifications menu in the navigation bar); email or push notification is out of scope.
- "Media area" means the user's own private file area for documents kept with records. It is separate from the vehicle images feature, and any screen for browsing it beyond the kept file on its record is defined in the plan.
- How record details are read from PDFs and images, which providers are used, and the specific rules for recognizing each record kind are decided in the plan.
- Ingestion creates only new records of existing kinds and never changes existing vehicles or records.
