# Research: Email Ingestion Intake

Decisions for the open technical questions left by the spec ("I'll give more specifics in the plan"). Every item follows existing patterns (features 005 and 006, vehicle images) unless noted. No `NEEDS CLARIFICATION` items remain.

## R1. How the Logic App authenticates

- **Decision**: Add an Entra **app role** `Email.Ingest` (allowed member type: Application) to the existing `MyGarage API` app registration. The Logic App calls with a client-credentials token (managed identity on the HTTP action, audience `api://<api-client-id>`). The intake endpoint uses a named authorization policy: authenticated, the `roles` claim contains `Email.Ingest`, and the token is app-only (no `scp` delegated scope). `Initialize-Entra.ps1` creates the role; the README documents assigning it to the Logic App's managed identity.
- **Rationale**: Satisfies FR-002 with no secret in source. The existing fallback policy requires the `access_as_user` scope, which an app-only token never carries, so the workflow cannot call any user endpoint and users cannot call intake (they have no role).
- **Alternatives**: Shared API key header: rejected (secret handling, rotation, not Entra). Delegated user token for the mailbox owner: rejected (would make the workflow a user, FR-002).

## R2. Request shape: JSON with base64 attachments

- **Decision**: `POST /api/intake/emails`, `application/json`. Fields: `messageId`, `receivedAt`, `from`, `subject`, `bodyText`, and `attachments[]` each with `fileName`, `contentType`, `isInline`, `contentBase64`. See [contracts/intake-email.example.json](contracts/intake-email.example.json). Limits: 22 MB request, 5 attachments, 10 MB each, 15 MB total decoded, text 100,000 characters, subject 500, `from` 320.
- **Rationale**: The Logic Apps Office 365 and Outlook connectors return attachments as `contentBytes` (base64) and the HTTP action sends JSON natively; multipart needs extra composition steps. A single JSON body keeps the contract simple and validated by one FluentValidation validator. Inline images (signature logos) are marked `isInline` and ignored.
- **Alternatives**: `multipart/form-data`: rejected (awkward in Logic Apps). Pre-uploading attachments to blob storage and sending links: rejected (workflow would need storage access to the private account, and the API would fetch caller-supplied URLs, an SSRF risk).

## R3. Acknowledge fast, process afterwards

- **Decision**: The endpoint validates, resolves the sender, stores the submission and attachments, and returns `202 Accepted` with `{ submissionId, status: "received" }`. A hosted `SubmissionProcessor` claims queued rows (`FOR UPDATE SKIP LOCKED`, a lease timestamp, up to 3 attempts with backoff of 1, 5, and 30 minutes) and runs extraction.
- **Rationale**: Model calls on PDFs take many seconds (SC-007 requires a 5 s acknowledgement). A database-backed queue survives restarts and multiple instances and adds no infrastructure.
- **Alternatives**: Extract inside the request: rejected (latency, Logic App timeouts, retries create duplicates). In-memory `Channel` only: rejected (lost on restart). Service Bus: rejected as unneeded infrastructure at this volume.

## R4. Acknowledgement never reveals sender matching

- **Decision**: The response body and status are identical for matched, unmatched, and duplicate submissions. The unmatched path records only `message_id`, `arrived_at`, and status `not_accepted`; it stores no sender, subject, text, or attachments and does not decode attachments beyond validation. Duplicate calls return a fresh acknowledgement with the existing `submissionId`.
- **Rationale**: FR-008 and SC-002: the endpoint must not become an oracle for who has an account. A refused-versus-accepted difference would be one.
- **Alternatives**: Return `422` for unknown senders: rejected (enumeration). Hash the sender into the not-accepted row: rejected (nothing is needed from it, and a hash of an email is still personal data).

## R5. Finding the user from the sender address

- **Decision**: There is no users table today (identity lives only in token claims). Add `user_profiles (owner_id, email, display_name, first_seen_at, last_seen_at)`, upserted by a small `UserProfileTracker` after authentication for any user request, at most once per owner per hour (in-memory throttle). Lookup is `lower(email) = lower(@sender)`; exactly one row matches or the submission is not accepted. The email comes from the existing `CurrentUserMapper` (`email` claim, else `preferred_username`).
- **Rationale**: Satisfies "look up in the application users". A user becomes matchable after their next sign-in following deployment. The `email` optional claim should be enabled on the API app (README note) because `preferred_username` is sometimes a UPN that differs from the mailbox address.
- **Alternatives**: Query Microsoft Graph for the sender: rejected (extra permission and a tenant-wide directory read; would also match people who never used My Garage). A user-managed list of sender addresses: rejected here (spec assumption: single account address).

## R6. What the AI is asked and what comes back

- **Decision**: One call per supported attachment (the email subject and text are included as context only), or one call for the text when there are no supported attachments. The call uses the Azure OpenAI **Responses API**: the PDF as an `input_file` with base64 `file_data`, images as `input_image` data URIs, instructions that treat all content as data, and **structured output** (`text.format` JSON schema, strict) returning:
  - `kind`: `service_record | fuel | energy | inspection | registration | insurance | tax | none`, plus `confidence` (0-1) and a short `notes`.
  - A flat set of nullable fields for every kind (dates as `yyyy-MM-dd`, money as decimals, no currency symbols), with service `lineItems[]` (description, flat cost, labor hours, labor rate, parts[name, quantity, unit price]).
  - `vehicleHint`: `vin`, `year`, `make`, `model`, `odometer`.
  - `none` means the document is not a vehicle maintenance document.
- **Rationale**: The Responses API accepts PDFs directly and gives the model both extracted text and a page image, so scanned PDFs work; Entra authentication is supported. A strict schema turns the answer into data the existing validators can judge. One document per call keeps drafts and costs predictable.
- **Alternatives**: Azure AI Document Intelligence then a second model call: rejected (two services; the model alone reads PDFs and images). Free-text answers parsed by hand: rejected (brittle). Sending the user's vehicle list to the model for matching: rejected (see R8).

## R7. Calling the model without an SDK package

- **Decision**: A typed `HttpClient` (`ResponsesApiExtractor`) posts JSON to `{endpoint}/openai/v1/responses`, authenticated with a bearer token from `DefaultAzureCredential` for scope `https://ai.azure.com/.default`, with the existing resilience handler, a 90 s timeout, and `store: false`. Settings: `EmailIntake:Extraction:Endpoint` and `:Deployment`. No new package is needed: `DefaultAzureCredential` already ships with the Azure SDK packages the solution references (verified at implementation time). When the endpoint is not configured, extraction is disabled (R9).
- **Rationale**: Avoids an experimental SDK surface and a package, keeps request and response shapes under test with a fake handler, and needs no key anywhere. Entra access is "recommended" in the service documentation.
- **Alternatives**: The `OpenAI` .NET SDK: rejected for now (adds a package and experimental-API warnings that are errors in this solution). API keys: rejected (secrets).

## R8. Treat all content as hostile (prompt injection and bad output)

- **Decision**: (a) The model has no tools and no access to data: it receives only the email and document and returns the schema. (b) The user's vehicles are never sent to the model; vehicle matching is done in code (`VehicleMatcher`) on the owner's own vehicles. (c) The model's output is parsed against the schema, trimmed, length-limited, and then validated by the same FluentValidation validators as the manual forms; invalid values become `missing` or `invalid` in the compatibility view, not records. (d) Nothing is stored as a record until the user accepts. (e) All extracted text is rendered escaped in Blazor and attachments are served with `nosniff` and no-store caching (images also under a sandboxing `Content-Security-Policy`; PDFs cannot be sandboxed, see R20). (f) The Azure OpenAI deployment's guardrails stay on; a `content_filter` rejection yields a manual-entry draft.
- **Rationale**: Email is attacker-controlled input and the model is a confusable deputy; limiting its capabilities and keeping a human approval step bounds the damage to a wrong draft the user can discard.

## R9. Failure and disabled-extraction behavior

- **Decision**: If extraction is not configured, fails after retries, or is blocked by a content filter, each valid attachment still becomes a draft in state `manual_entry` with no kind, so the user can pick a type and type values from the original file. If the model answers `none` for an attachment, that attachment produces no draft (`not_applicable`) unless its confidence is below 0.2 or it returned no readable content, in which case it also becomes a `manual_entry` draft. Text-only submissions with `none` or no result become `not_applicable`; text-only submissions that fail all retries become `failed`.
- **Rationale**: Spec US4 scenario 3 and FR-019: never lose a user's document because the model had a bad day, but do not spam users with noise.

## R10. Blob storage layout and consistency

- **Decision**: Two new private containers created by a hosted service like `ImageContainerHostedService`: `email-intake` (name `{submissionId}/{attachmentId}`) and `vehicle-documents` (name `{vehicleId}/{documentId}`). Intake uploads blobs first, then commits rows in one transaction; if the transaction fails or loses a duplicate race, the just-uploaded blobs are deleted. An hourly sweep deletes blobs with no row older than one hour. Accepting with "keep original": copy blob to `vehicle-documents`, then commit (record, draft state, document row) in one transaction; on failure delete the copy; after commit delete the intake blob. All reads and deletes first run an ownership-checked SQL statement (`email_attachments.owner_id`, or `vehicle_documents` joined to `vehicles.owner_id`).
- **Rationale**: Constitution VI (private blobs, ownership in SQL before blob access, metadata and blob consistent). A separate container keeps originals apart from vehicle images as requested.
- **Alternatives**: Same container with prefixes: rejected (the request asks for separate storage and separate lifecycles). Streaming the file to the browser through a SAS link: forbidden by the constitution.

## R11. Drafts, kinds, and the proposal

- **Decision**: One `maintenance_drafts` row per source (text or one attachment). The proposal is stored as `jsonb` text of a `DraftProposal` DTO (Shared) with one populated section per kind: `serviceRecord`, `fuelEnergy`, `inspection`, `registration`, `insurance`, `tax`. The Database project stores and returns it as text and never interprets it (it cannot reference Shared). The Api validates it with the existing per-kind validators on accept. Changing the kind re-maps overlapping fields (date, odometer, total or cost, shop) into the new section and clears the rest.
- **Rationale**: Six kinds with different fields do not justify six draft tables; `jsonb` keeps one list, one notification path, and one review page. Reusing validators guarantees an accepted draft is as valid as a hand-entered record.

## R12. Service record proposals and line items

- **Decision**: The model's line items map to the existing structure: if labor hours and rate are present, or parts are present, the line is a labor/parts line (flat cost cleared); otherwise it is a flat cost. A line with only a total becomes a flat-cost line. Parts keep name, quantity, and unit price. The proposal's service header is date, odometer, and shop. Odometer is required by a service record; when absent the compatibility view marks it `missing` and the user must enter it before accepting.
- **Rationale**: Matches the existing `service_line_items` check constraints (flat cost or labor/parts, never both) so a proposal that passes validation always inserts.

## R13. The compatibility view

- **Decision**: `CompatibilityChecker` runs the existing validators on the current proposal for its kind and returns, per field and per line item, `ok`, `missing` (empty required value), or `invalid` (rule broken, with the validator's message), plus advisory `warnings` (no line items found, total of lines differs from the document total, probable duplicate of an earlier email attachment by SHA-256 or of a record with the same date and total). The Web page shows it beside the original document and recalculates when the user edits. A draft is acceptable only when no field is `missing` or `invalid`.
- **Rationale**: Delivers "see the proposed match for compatibility as a service record" with no second rule set.

## R14. Vehicle matching

- **Decision**: `VehicleMatcher` receives the owner's vehicles (id, year, make, model, VIN) and the hint. Order: exact VIN; else a unique year + make + model match; else a unique make + model match; else the owner's only vehicle (flagged `only_vehicle`); else none. The method is stored (`vin | description | only_vehicle | none`) and shown to the user, who can always change the vehicle. The model never sees the vehicle list.
- **Rationale**: Ownership stays in SQL and code, not in a prompt (R8, FR-009).

## R15. Notifications

- **Decision**: A new `notifications` table and feature. Creating a draft inserts a notification in the same transaction ("New draft service record ready to review", with the draft id and no sender or subject). The existing bell in `AppNavbar` becomes `NotificationsMenu`: badge with the unread count, the latest five in the dropdown, "Mark all read" and a link to `/drafts`; it refreshes every 60 seconds and after navigation. Opening a draft marks its notification read. Deciding or expiring a draft removes its notification.
- **Rationale**: The bell is currently a static placeholder; this fills it without new infrastructure. Server-driven polling suits Blazor Server and avoids a push service.
- **Alternatives**: SignalR push: rejected (Blazor Server already has a circuit but cross-circuit fan-out adds state). Email or push notification: out of scope per the spec.

## R16. Review lifecycle and retention

- **Decision**: A draft expires 30 days after creation (`expires_at`). An hourly `DraftExpiryService` marks expired drafts, deletes their intake blobs, clears `proposal`, and clears the submission's sender, subject, and text when no draft of that submission remains undecided. Accepting or discarding clears the same data immediately. Kept originals persist as vehicle documents until the user deletes them or the vehicle. `email_submissions` rows themselves keep only `message_id`, times, status, and `owner_id` for duplicate detection and history.
- **Rationale**: FR-017 and FR-028 (30 days; content removed on decision or expiry; history survives).

## R17. Attachment safety

- **Decision**: A shared `FileSignature` in `Api/Common` recognizes PDF (`%PDF-`), JPEG, PNG, and WebP by bytes (extending the idea of the existing `ImageSignature`); the stated content type and file extension are ignored. Files must also decode from base64 and be within size limits. File names are reduced to a safe display name and never used as a storage path. Malware scanning is recommended at the storage account (Defender for Storage) and noted in the infra README; the app never executes or renders attachment content except as a same-origin PDF (in the browser's isolated PDF viewer) or image, served only to its owner.
- **Rationale**: FR-018, FR-006. The browser PDF viewer is the least risky renderer available without new dependencies.

## R18. Idempotency and concurrency

- **Decision**: `email_submissions.message_id` is unique. The intake insert uses `INSERT ... ON CONFLICT (message_id) DO UPDATE ... WHERE status = 'failed'` to requeue failed ones and do nothing otherwise. Draft decisions use a guarded `UPDATE ... WHERE id = @id AND owner_id = @owner AND state IN ('needs_review','manual_entry') RETURNING` as the first statement of the accept transaction; no row means already decided or not found (409 or 404).
- **Rationale**: FR-024, SC-005: exactly-once effects under retries and concurrent clicks.

## R19. Rate limiting

- **Decision**: Built-in ASP.NET Core rate limiter on the intake group: fixed window 120 requests per minute (above the 50 per minute burst target), returning `429` with `Retry-After`; the Logic App retry policy handles it.
- **Rationale**: Bounds abuse by a compromised workflow credential at no dependency cost.

## R20. Showing the original in the browser

- **Decision**: The browser never calls the Api. The Web app proxies `GET /drafts/{id}/attachment` and `/documents/{vehicleId}/{documentId}` (like `VehicleImageEndpoints`), streaming from the Api with `X-Content-Type-Options: nosniff`, `Content-Disposition: inline`, and `Cache-Control: private, no-store`. PDFs also carry `X-Frame-Options: SAMEORIGIN` and are embedded in a plain, titled `<iframe>` (no `sandbox` attribute, no `Content-Security-Policy: sandbox` header); images are embedded in an `<img>` and keep `Content-Security-Policy: sandbox`. The review page always offers a Download link as a fallback.
- **Rationale**: Consistent with how vehicle pictures are served; the API stays internal. Chrome's built-in PDF viewer does not render inside a sandboxed iframe or in a response carrying `Content-Security-Policy: sandbox`, so the original sandbox-for-PDFs decision made the preview blank. The browser's PDF viewer already runs in an isolated process, and the file is the user's own attachment, served only to its owner (ownership is checked in the Api), so dropping the sandbox for PDFs does not widen what the owner can see; `nosniff` and the stored, byte-verified content type still stop the file being treated as anything but a PDF.

## R21. Infrastructure and configuration

- **Decision**: `AppHost` passes optional `EmailIntake__Extraction__Endpoint` and `EmailIntake__Extraction__Deployment` to the `api` project from AppHost configuration (user-secrets), reusing the existing storage `blobs` resource for both new containers. The Azure OpenAI resource, a vision- and PDF-capable deployment, and the Logic App are provisioned outside this repository. The deployment identity needs `Cognitive Services OpenAI User` on the Azure OpenAI resource and `Storage Blob Data Contributor` on the storage account.
- **Rationale**: No emulator exists for the model; local runs work with extraction disabled (R9) or against a real deployment.
