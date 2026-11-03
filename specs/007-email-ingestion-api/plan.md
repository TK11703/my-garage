# Implementation Plan: Email Ingestion Intake

**Branch**: `007-email-ingestion-api` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/007-email-ingestion-api/spec.md`

## Summary

An external Logic App watches a mailbox and, for each message, calls one new API endpoint with a structured JSON body (message id, sender, received time, subject, plain text, and PDF or image attachments as base64). The API authenticates the Logic App by an Entra **app role** (not a user), validates the body, looks the sender up among known users, stores the submission and attachments durably, and answers `202 Accepted` with the same body whether or not the sender matched. A background worker then asks an AI model (Azure OpenAI Responses API, PDF and image input, structured JSON output) what each attachment or text is: a service record with line items and parts, a fuel or charging receipt, an inspection, a registration, an insurance policy, or a tax. The result is never a record: it is a **draft maintenance item** with a proposed kind, proposed vehicle, and proposed values, and a notification in the navigation bar bell.

The user opens the draft beside the original PDF or image, sees how well the proposal fits (for a service record: date, odometer, shop, line items, parts, totals, and which required details are found, missing, or doubtful), edits it, optionally changes its type, chooses whether to keep the original file, and **accepts** (creates the real record atomically, using the same validation rules as the manual forms) or **discards** it. Kept originals go to a separate private blob container and appear in a new Documents section of the Maintenance tab, apart from vehicle pictures. Unreviewed drafts expire after 30 days and all email content is purged once a draft is decided.

Technical approach: one forward-only migration (`0012`) adding `user_profiles`, `email_submissions`, `email_attachments`, `maintenance_drafts`, `notifications`, `vehicle_documents`, plus a `from_email` flag on the six record tables. New feature folders `EmailIntake`, `Drafts`, `Notifications`, `VehicleDocuments` in Shared, Database, Api, and Web. The work queue is the `email_submissions` table (claimed with `FOR UPDATE SKIP LOCKED`), so no queue infrastructure is added. The AI call is a typed `HttpClient` against the Responses REST API with Entra authentication (`DefaultAzureCredential`, already available through the existing Azure SDK dependencies); no new package is added. Everything the model returns is treated as untrusted input, validated by the existing FluentValidation validators, and shown to the user for approval.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (pinned by `global.json`)

**Primary Dependencies**: Existing: Aspire 13.x, `Microsoft.Identity.Web`, FluentValidation, ASP.NET Core OpenAPI and rate limiting (built in), Npgsql, Dapper, `Azure.Storage.Blobs`, `Microsoft.Extensions.Http.Resilience`, Serilog, OpenTelemetry, Bootstrap 5.3.x. **New**: none (`DefaultAzureCredential` for the AI endpoint comes with the existing Azure SDK dependencies; see [research.md](research.md) R7). No AI SDK package.

**Storage**: PostgreSQL (migration `0012_create_email_intake.sql`) and the existing Azure Blob storage account with two new private containers: `email-intake` (attachments waiting for review) and `vehicle-documents` (originals the user kept). Vehicle pictures stay in `vehicle-images`, untouched.

**Testing**: xUnit; Testcontainers PostgreSQL and Azurite for `MyGarage.Database.Tests` and `MyGarage.Api.Tests` (no mocked database); a fake extractor and a fake `HttpMessageHandler` for the AI boundary; bUnit and `WebApplicationFactory` for `MyGarage.Web.Tests`; `UiStandardConformanceTests` extended for new pages. Docker required for database and API tests.

**Target Platform**: Local development with Aspire; Azure for the Logic App and the Azure OpenAI deployment. The Logic App and its mailbox are outside this repository.

**Project Type**: Web application (Blazor Web App + minimal API + PostgreSQL + blob storage), Aspire-orchestrated, with one machine-to-machine endpoint.

**Performance Goals**: Acknowledge `POST /api/intake/emails` in under 5 s for 95% of calls (SC-007), because extraction runs afterwards. Drafts visible within 60 s of receipt for text and 2 min for a PDF under normal load. Review list first page under 2 s.

**Constraints**: Caller must hold the `Email.Ingest` app role; all existing endpoints keep refusing app-only tokens. Body at most 22 MB, at most 5 attachments, 10 MB each, 15 MB total decoded, text at most 100,000 characters. Attachment type decided from file signature. The workflow's acknowledgement never reveals whether a sender matched. No email text, sender, subject, attachment content, or extracted values in logs. Blobs private, never addressable by URL, ownership proved in SQL before any read, write, or delete. Every create from a draft goes through the same validators as the manual forms. All SQL parameterized in `.sql` files; data access async with `CancellationToken`.

**Scale/Scope**: Tens of users, up to 50 submissions per minute in a burst, a handful of drafts per user per week. About 17 new endpoints, 3 background services, 6 new tables, 3 new Web pages, one navbar component, one Documents section, one migration.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. .NET 10 / C# only; nullable on; warnings as errors; async + `CancellationToken` | PASS | All repositories, services, endpoints, and background loops are async and forward tokens. |
| II. Feature-folder organization | PASS | New `EmailIntake/`, `Drafts/`, `Notifications/`, `VehicleDocuments/` folders in Shared, Database (plus `Scripts/<Feature>/`), Api, Web, and mirrored in tests. `user_profiles` lives in `Users/` of Database and Api. Cross-feature helpers (file signature detection) go to `Common/`. |
| III. Solution structure & dependency direction | PASS | No new projects or references. Database holds repositories, SQL, and both blob stores and does not reference Shared, so draft proposals are stored as `jsonb` text that the Api serializes with Shared DTOs. Web reaches data only through the Api. |
| IV. DTO boundary | PASS | New request/response DTOs and enums in `MyGarage.Shared`; database records stay internal; explicit mappers with tests. |
| V. Validated minimal APIs | PASS | One `MapGroup` per feature; every request DTO has a FluentValidation validator through `ValidationFilter<T>`; `ProblemDetails` for errors; OpenAPI metadata declared. The intake endpoint validates before touching storage. |
| VI. Dapper, PostgreSQL, SQL scripts, private blobs | PASS | All SQL in `Scripts/<Feature>/`. Attachment rows carry `owner_id` and vehicle documents join `vehicles.owner_id`; every blob read, write, and delete is preceded by an ownership-checked SQL statement; no public address or SAS. Row and blob are kept consistent by upload-then-commit with an orphan sweep (research R10). |
| VII. Versioned migrations | PASS | `0012_create_email_intake.sql`, forward-only; `0001`-`0011` untouched. |
| VIII. Test-first, real dependencies | PASS | Failing tests first for migration, repositories, validators, intake endpoint (success, validation failure, unauthorized, wrong role, duplicate, unmatched), worker, extractor client, accept/discard, and Web pages. Real PostgreSQL and Azurite; only the external AI service is replaced by a fake. |
| Auth: Entra OIDC + JWT; every endpoint authorized | PASS | The intake endpoint is authorized by a named policy requiring the `Email.Ingest` app role, not anonymous. All user endpoints inherit the existing fallback policy. |
| Observability | PASS | Logs carry ids, counts, statuses, and durations only. New metrics: submissions by outcome, extraction duration and failures, draft decisions. |
| Data protection: ownership from claims; SQL scoped by owner; child records through parent | PASS (amended v1.3.0) | The intake endpoint resolves the owner from the sender address supplied by the trusted workflow, which the constitution's trusted service intake exception now allows. Conditions and how they are met are in Complexity Tracking. Everything after intake (review, accept, discard, documents) derives the owner from claims as usual. |
| Dependencies justified | PASS | No new packages (research R7). |

**Post-design re-check (after Phase 1)**: unchanged. The owner-from-sender rule is covered by the v1.3.0 amendment; one cross-feature change (`from_email` flag) and one new package are tracked below.

## Project Structure

### Documentation (this feature)

```text
specs/007-email-ingestion-api/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── api.openapi.yaml           # Intake, drafts, notifications, documents
│   ├── intake-email.example.json  # Proposed Logic App request body
│   └── web-routes.md              # New pages, proxy route, navbar behavior
└── tasks.md             # Phase 2 output (/speckit-tasks - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
infra/entra/
├── Initialize-Entra.ps1                  # + app role Email.Ingest on the API app
└── README.md                             # + granting the role to the Logic App identity

src/
├── MyGarage.AppHost/AppHost.cs           # + optional AI endpoint/deployment settings passed to api
├── MyGarage.Shared/
│   ├── EmailIntake/                      # IntakeEmailRequest, IntakeAttachmentRequest, IntakeAcknowledgement, EmailHistoryItemDto
│   ├── Drafts/                           # MaintenanceDraftDto/SummaryDto, DraftProposal (+ per-kind sections), DraftKind, DraftState,
│   │                                     # CompatibilityDto, AcceptDraftRequest, ChangeDraftKindRequest, VehicleSuggestionDto
│   ├── Notifications/                    # NotificationDto, UnreadCountDto
│   ├── VehicleDocuments/                 # VehicleDocumentDto
│   └── (six existing record DTO folders) # + FromEmail on summary/detail DTOs
├── MyGarage.Database/
│   ├── Users/                            # UserProfileRepository (upsert, find by email)
│   ├── EmailIntake/                      # EmailSubmissionRepository, AttachmentRepository, IIntakeBlobStore + AzureBlobIntakeStore
│   ├── Drafts/                           # MaintenanceDraftRepository, DraftAcceptanceRepository (one transaction, reuses existing insert scripts)
│   ├── Notifications/                    # NotificationRepository
│   ├── VehicleDocuments/                 # VehicleDocumentRepository, IVehicleDocumentBlobStore + AzureBlobVehicleDocumentStore
│   ├── ServiceRecords|FuelEnergyEntries|Inspections|Registrations|Insurance|Taxes/   # + from_email in insert/select scripts
│   └── Scripts/
│       ├── Migrations/0012_create_email_intake.sql
│       ├── Users/  EmailIntake/  Drafts/  Notifications/  VehicleDocuments/
├── MyGarage.Api/
│   ├── Common/                           # FileSignature (PDF + images), EmailIntakeAuthorization (policy), rate limiter setup
│   ├── Users/                            # UserProfileTracker (throttled upsert after authentication)
│   ├── EmailIntake/                      # IntakeEndpoints, IntakeRequestValidator, SubmissionProcessor (BackgroundService),
│   │   └── Extraction/                   #   IDraftExtractor, ResponsesApiExtractor, ExtractionSchema, VehicleMatcher, ProposalMapper
│   ├── Drafts/                           # DraftsEndpoints, DraftService (accept/discard/change kind), CompatibilityChecker, validators, mapper
│   ├── Notifications/                    # NotificationsEndpoints, mapper
│   ├── VehicleDocuments/                 # VehicleDocumentsEndpoints, mapper
│   └── Vehicles/VehicleService.cs        # delete flow also removes vehicle documents and detaches drafts
└── MyGarage.Web/
    ├── Notifications/                    # NotificationsMenu.razor (bell, badge, 60 s refresh), NotificationsApiClient
    ├── Drafts/                           # DraftsListPage, DraftReviewPage, ServiceRecordProposalEditor, CompatibilityPanel,
    │                                     # DraftsApiClient, DraftAttachmentEndpoints (same-origin proxy)
    ├── VehicleDocuments/                 # DocumentsSection (Maintenance tab), client, content proxy
    ├── Maintenance/MaintenanceView.razor # + Documents section; "From email" badge in the six lists
    └── Components/Layout/AppNavbar.razor # bell replaced by NotificationsMenu; "Email drafts" in the account menu

tests/
├── MyGarage.Database.Tests/   # EmailIntake, Drafts, Notifications, VehicleDocuments, Users, migration 0012
├── MyGarage.Api.Tests/        # intake endpoint, worker, extractor client, drafts, notifications, documents, auth matrix
├── MyGarage.Web.Tests/        # bell, drafts list, review page, compatibility panel, documents section, UI conformance
└── MyGarage.AppHost.Tests/    # containers created, intake endpoint reachable
```

**Structure Decision**: Four new feature folders mirrored in each project, matching the layout of features 005 and 006. Intake and review are separate features because they have different callers (a machine versus a signed-in user) and different authorization. The accept-from-draft transaction lives in `Database/Drafts` and reuses the existing per-kind insert scripts so rules and totals are never duplicated.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Owner is resolved from a request-supplied sender address on the intake endpoint (allowed by the Data protection "trusted service intake exception", v1.3.0) | The mailbox receives mail from users; the only available identity is the sender address, which the spec requires matching to a known user (FR-007). | Per-user tokens or addresses were rejected at specification time. Mitigations: only the `Email.Ingest` app role may call; the address is a lookup key, never proof; a non-unique match is refused; the result is only a draft; the matched user must approve before any record exists; the acknowledgement never reveals a match. The amendment (v1.3.0, 2026-11-03) permits this for trusted service callers under conditions (a)-(e); this feature satisfies each of them. |
| `from_email` column and `FromEmail` field added to six existing record tables and DTOs | FR-014 requires created records to be identifiable as coming from email, shown in existing lists. | Joining `maintenance_drafts` in every list query couples six features to one; a boolean is additive, default `false`, and leaves existing behavior unchanged. |
| Polymorphic `source_record_id` on `vehicle_documents` with no foreign key | One document table serves six record kinds. | Six link tables or six nullable foreign keys is more schema for no benefit; documents stay available if the record is later deleted, which the user can see and remove from the Documents section. |
