# Implementation Plan: Upload a Maintenance Record

**Branch**: `011-upload-maintenance-record` | **Date**: 2026-11-05 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/011-upload-maintenance-record/spec.md`

## Summary

A signed-in user hands My Garage a maintenance document (PDF or image) directly instead of by email. Two entry points open the same upload page: an **Add Maintenance** button on the main screen, and an **Import Record** button in a vehicle's maintenance tab that carries the vehicle along so the draft starts with that vehicle chosen. The file does not get a parallel pipeline. A new user endpoint, `POST /api/drafts/uploads`, stores the accepted files in the existing private intake container and queues one **synthetic email submission** (origin `upload`, no sender, subject, or text) owned by the signed-in user. The existing `SubmissionProcessor` then reads each file with the existing extractor, applies the existing vehicle matching, and creates ordinary draft maintenance items and notifications. The review page, compatibility panel, kind change, keep-original option, accept, discard, 30-day expiry, duplicate indication, and history are all reused unchanged. Records created from uploaded drafts keep the existing from-email mark (the user asked for an email-based draft).

New code is the thin edge only: a validated multipart endpoint with a per-user rate limit, a shared helper extracted from `IntakeService` so email and upload store attachments the same way, two columns on `email_submissions` (`origin`, `preselected_vehicle_id`), one new vehicle-match value (`selected`), an `Origin` field on the draft and history DTOs, one Web upload page, and the two buttons. No new package, project, container, background service, or extraction code.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (pinned by `global.json`)

**Primary Dependencies**: Existing only: ASP.NET Core minimal APIs and rate limiting, FluentValidation, Npgsql, Dapper, `Azure.Storage.Blobs`, Serilog, OpenTelemetry, Blazor static SSR, Bootstrap 5.3.x. **New**: none.

**Storage**: PostgreSQL (forward-only migration `0015_add_upload_origin.sql`) and the existing `email-intake` private blob container. No new container: uploaded files wait in the same place as email attachments and move to `vehicle-documents` only if the user keeps them on accept, as today.

**Testing**: xUnit; Testcontainers PostgreSQL and Azurite for `MyGarage.Database.Tests` and `MyGarage.Api.Tests` (no mocked database); the existing fake `IDraftExtractor` for the AI boundary; bUnit and `WebApplicationFactory` for `MyGarage.Web.Tests`; `UiStandardConformanceTests` covers the new page. Docker required for database and API tests.

**Target Platform**: Local development with Aspire; Azure for the deployed app. Nothing in `infra/` changes.

**Project Type**: Web application (Blazor static SSR + minimal API + PostgreSQL + blob storage), Aspire-orchestrated.

**Performance Goals**: `POST /api/drafts/uploads` answers in under 5 s for 95% of calls because reading happens afterwards in the existing background processor; a single-page invoice appears as a draft within the existing targets (60 s text, 2 min PDF). Spec SC-001 (file chosen to proposed record under 1 minute) relies on the same processor latency.

**Constraints**: User tokens only (existing app-only refusal stays). Per upload: at most 5 files, 10 MB per file, 15 MB total, no looser than email. Type decided from file signature, never from name or claimed type. Owner and vehicle ownership proved server-side; `vehicleId` not owned by the caller answers 404 and stores nothing. No file name, content, or extracted value in logs. All SQL parameterized in `.sql` files; data access async with `CancellationToken`. Rate limit per signed-in user (default 10 uploads a minute).

**Scale/Scope**: Tens of users, a few uploads per user per week. One new endpoint, one extracted helper, one migration, two modified SQL scripts plus history/draft read scripts, one Web page, one Web client method, two buttons.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. .NET 10 / C# only; nullable on; warnings as errors; async + `CancellationToken` | PASS | All new code async and forwards tokens. |
| II. Feature-folder organization | PASS | Endpoint, service, validator, options in `MyGarage.Api/Drafts/Uploads/`; page and handler in `MyGarage.Web/Drafts/`; DTOs in `MyGarage.Shared/Drafts/`; SQL stays in `Scripts/EmailIntake/` and `Scripts/Drafts/`. The extracted attachment helper goes to `MyGarage.Api/EmailIntake/` where both callers can use it. Tests mirror these folders. |
| III. Solution structure & dependency direction | PASS | No new project or reference. Web reaches data only through the Api (`DraftsApiClient`); Database does not reference Shared (origin crosses the boundary as text and is mapped in the Api, like `vehicle_match`). |
| IV. DTO boundary | PASS | New `DraftUploadResultDto` and `DraftOrigin` in `MyGarage.Shared`; records stay internal; explicit mapper changes with tests. |
| V. Validated minimal APIs | PASS | `MapGroup("/api/drafts")` extended; request DTO validated by FluentValidation through `ValidationFilter<T>` before any storage; `ProblemDetails` errors; OpenAPI metadata declared (see `contracts/`). |
| VI. Dapper, PostgreSQL, SQL scripts, private blobs | PASS | Reuses `IIntakeBlobStore` (private, no public address or SAS), ownership proved in SQL before every read, write, or delete; the preselected vehicle is kept only when `vehicles.owner_id` matches (as `InsertDraft.sql` already does). Upload-then-commit with the existing orphan sweep keeps rows and blobs consistent. |
| VII. Versioned migrations | PASS | `0015_add_upload_origin.sql`, forward-only; `0001`-`0014` untouched. |
| VIII. Test-first, real dependencies | PASS | Failing tests first for migration, repository, validator, endpoint (success, validation failure, not-found vehicle, unauthorized, wrong role, rate limit), processor preselection, mapper, Web page and handler. Real PostgreSQL and Azurite; only the AI service is faked. |
| Auth: Entra OIDC + JWT; every endpoint authorized | PASS | The endpoint inherits the fallback policy for signed-in users and refuses app-only tokens. It is not a trusted-service endpoint, so the v1.3.0 intake exception is not used. |
| Observability | PASS | Logs carry ids, counts, and outcomes only. The existing intake metrics gain an `origin` tag on submission outcomes. |
| Data protection: ownership from claims; SQL scoped by owner | PASS | The owner comes only from validated claims; nothing in the request names an owner. The vehicle id is verified against the owner before anything is stored. Everything after queueing is the existing owner-scoped flow. |
| Dependencies justified | PASS | No new packages. |

**Post-design re-check (after Phase 1)**: unchanged. No Complexity Tracking entries.

## Project Structure

### Documentation (this feature)

```text
specs/011-upload-maintenance-record/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── api.openapi.yaml   # POST /api/drafts/uploads and the changed draft/history schemas
│   └── web-routes.md      # /drafts/upload page, buttons, query values, messages
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── MyGarage.Shared/
│   └── Drafts/                           # DraftOrigin; DraftUploadResultDto (+ refused entries); VehicleMatch.Selected;
│                                         # Origin on MaintenanceDraftDto/SummaryDto; Origin on EmailHistoryItemDto (Shared/EmailIntake)
├── MyGarage.Database/
│   ├── EmailIntake/                      # EmailSubmissionRecord: Origin, PreselectedVehicleId
│   ├── Drafts/                           # read records carry origin
│   └── Scripts/
│       ├── Migrations/0015_add_upload_origin.sql
│       ├── EmailIntake/                  # InsertSubmission, ClaimNext, ListHistoryPage: origin + preselected vehicle
│       └── Drafts/                       # GetById, ListOpenPage: origin
├── MyGarage.Api/
│   ├── Common/                           # DraftUploadRateLimiting (per-user policy, same shape as sticker import)
│   ├── EmailIntake/                      # + IntakeAttachments (shared detect/hash/name/store logic extracted from IntakeService)
│   │   └── Extraction/VehicleMatcher.cs  # + optional preselected vehicle -> VehicleMatch.Selected
│   │   └── SubmissionProcessor.cs        # passes the submission's preselected vehicle to the matcher
│   └── Drafts/
│       ├── DraftsEndpoints.cs            # + MapPost("/uploads")
│       ├── DraftMapper.cs / DraftKinds.cs# origin and the 'selected' match text
│       └── Uploads/                      # DraftUploadRequest, DraftUploadRequestValidator, DraftUploadService, DraftUploadOptions
└── MyGarage.Web/
    ├── Home/Home.razor                   # + "Add Maintenance" button
    ├── Maintenance/MaintenanceView.razor # + "Import Record" button (vehicle carried in the link)
    └── Drafts/
        ├── DraftUploadPage.razor         # /drafts/upload, named SSR form, per-file results
        ├── DraftUploadHandler.cs         # reads Request.Form.Files, checks limits, calls the Api
        ├── DraftsApiClient.cs            # + UploadAsync
        ├── DraftsListPage.razor          # + "uploaded" notice, origin label
        ├── DraftReviewPage.razor         # + upload block in place of the email block
        └── DraftFormat.cs                # origin and 'selected' match labels

tests/
├── MyGarage.Database.Tests/   # migration 0015 (count bumps), submission insert/claim with origin and preselected vehicle, history origin
├── MyGarage.Api.Tests/        # upload endpoint, validator, processor with preselection, mapper, auth/rate-limit, email intake regression
└── MyGarage.Web.Tests/        # upload page and handler, both buttons, list and review labels, UI conformance
```

**Structure Decision**: Extend the existing `Drafts` feature in every project rather than add a feature folder: an upload is just another way to start a draft. The synthetic submission means the whole processing, review, retention, and history path is shared with email, so the only new server code is the endpoint and its service.

## Complexity Tracking

No constitution violations to justify.
