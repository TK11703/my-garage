# Implementation Plan: Vehicle Tracking

**Branch**: `002-vehicle-tracking` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-vehicle-tracking/spec.md`

## Summary

Introduce persistence and the first real domain feature. A signed-in user gets a paged, card-based garage (picture, nickname, make, model, mileage) with search and filters, can add/edit/remove vehicles (nickname, make, model, trim, color, mileage, doors), attach free-text tags, keep a journal of notes each with an optional free-text category, and build a small gallery of pictures per vehicle (one is the main picture on the card). Details, tags, notes, and image metadata are stored in PostgreSQL; image files are stored in a private Azure Blob Storage account. Everything is strictly scoped to the authenticated owner.

Technical approach: add `MyGarage.Database` (Npgsql + Dapper, embedded `.sql` scripts, custom forward-only migration runner with checksum ledger, plus the blob-backed image store) and PostgreSQL and Azure Storage (Azurite emulator locally) resources in the Aspire AppHost; add `Vehicles` feature folders to Shared (DTOs), Api (minimal-API groups, FluentValidation, owner-scoped repository calls), Database (repositories, scripts, migrations), and Web (static-SSR pages using Bootstrap cards, a GET search/filter form, forms posted through the existing token-forwarding API client, and an authenticated image proxy). Image bytes never reach the browser except through authenticated endpoints; no storage URLs or SAS tokens are handed out.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (pinned by `global.json`)

**Primary Dependencies**: Existing: Aspire 13.x, `Microsoft.Identity.Web`, FluentValidation, ASP.NET Core OpenAPI, Serilog, OpenTelemetry. New (justified in [research.md](research.md)): Npgsql (via `Aspire.Npgsql` in Api; direct in Database), Dapper, `Aspire.Azure.Storage.Blobs` (Azure.Storage.Blobs client with Aspire configuration, health, and tracing), `Aspire.Hosting.PostgreSQL` and `Aspire.Hosting.Azure.Storage` (AppHost), `Testcontainers.PostgreSql` and `Testcontainers.Azurite` (tests). No image-processing library.

**Storage**: PostgreSQL (Aspire-hosted container with persistent data volume in development) for `vehicles`, `vehicle_tags`, `journal_notes`, `vehicle_images` (metadata only), and the `schema_migrations` ledger. Azure Blob Storage (Azurite emulator with a persistent volume locally; a storage account in Azure) for image files in a private container `vehicle-images`.

**Testing**: xUnit; Testcontainers PostgreSQL and Azurite for `MyGarage.Database.Tests` and `MyGarage.Api.Tests` (real database and real blob endpoint, no mocks); bUnit and `WebApplicationFactory` for `MyGarage.Web.Tests`; Aspire testing smoke test extended to the database and storage resources. Docker is required to run tests.

**Target Platform**: Local development with Aspire on Windows/macOS/Linux with Docker or Podman; Entra setup from feature 001 unchanged. In Azure, storage is accessed with the API's managed identity (no keys in configuration).

**Project Type**: Web application (Blazor Web App UI + minimal API + PostgreSQL + Blob Storage), Aspire-orchestrated.

**Performance Goals**: Garage page HTML and data < 2 s for garages up to 100 vehicles, including with search/filters (SC-002, SC-009); picture upload visible < 10 s (SC-005); tag/note add < 30 s of user effort (SC-008).

**Constraints**: Owner derived only from validated token claims; every owner-specific SQL statement filters on `owner_id`; no entity types across the API boundary; parameterized SQL only, in `.sql` files (search text is passed as a parameter with LIKE metacharacters escaped); no sensitive data in logs; image content verified by signature; blob container is private (no public access, no SAS URLs, no shared-key access in Azure) and images are streamed through authenticated endpoints; max 5 MB per image, 10 images per vehicle, 20 tags per vehicle, 4,000 characters per note, 40 characters per note category.

**Scale/Scope**: Tens of vehicles per user; 1 new project + 1 test project; 16 API endpoints across 4 sub-resources; 4 Web pages + 1 image endpoint; 4 migrations.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. .NET 10 / C# only; nullable on; warnings as errors; async + `CancellationToken` | PASS | Inherited from `Directory.Build.props`; every repository, service, endpoint, and client method takes `CancellationToken`. |
| II. Feature-folder organization | PASS | `Vehicles/` (with sub-folders `Tags/`, `JournalNotes/`, `Images/` only where code is large enough) in Shared, Api, Database, Web, and each test project; SQL in `Scripts/Vehicles/`, `Scripts/Tags/`, `Scripts/JournalNotes/`, `Scripts/VehicleImages/`; cross-feature code (script store, migration runner, paging, owner helper) in `Common/`. |
| III. Solution structure & dependency direction | PASS (with one interpretation, see Complexity Tracking) | `MyGarage.Database` is added now by the first feature that needs persistence; it references no MyGarage project. Api -> Shared + Database + ServiceDefaults; Web -> Shared + ServiceDefaults; Web reaches data only over HTTP. New `MyGarage.Database.Tests` mirrors Database feature folders. AppHost gains only PostgreSQL and storage resources and references. The blob-backed image store is treated as a repository in `MyGarage.Database` (persistence layer). |
| IV. DTO boundary | PASS | Database returns internal-to-Api data records (never exposed); Api maps explicitly to Shared DTOs; mapping covered by tests. Journal category is a plain optional `string` in both Database and Shared. |
| V. Validated minimal APIs | PASS | One `MapGroup` per feature (`/api/vehicles` with nested tags/notes/images groups); FluentValidation for every input (including paging, search/filter query, and multipart upload); `ProblemDetails` for 400/401/403/404/409/413; OpenAPI metadata on all endpoints. |
| VI. Dapper, PostgreSQL, SQL scripts | PASS | SQL only in embedded `.sql` files, loaded and cached by a script store; parameterized; repositories in Database; Api never runs SQL. |
| VII. Versioned migrations | PASS | Numbered forward-only scripts in `Scripts/Migrations/`, ledger table with checksum, runner fails on checksum drift. |
| VIII. Test-first, real dependencies | PASS | Testcontainers PostgreSQL and Azurite for repository, migration, and API tests; success + validation + not-found tests for every endpoint; bUnit for components with logic. |
| Auth: Entra OIDC (Web) + JWT bearer (Api); every endpoint authorized | PASS | New endpoints inherit the fallback policy; no anonymous endpoint added. Web pages and the image proxy require sign-in via the existing fallback policy. Storage is reachable only by the API's identity. |
| Observability: Serilog + OpenTelemetry | PASS | Npgsql and Azure SDK instrumentation added through Aspire client integrations; logs carry ids and counts only (never search text, tag/note text, or image names/bytes). |
| Data protection: ownership from claims; SQL scoped by owner; child ownership via parent | PASS | `owner_id` column; all vehicle queries (including search/filter and filter options) filter on it; tags, notes, and images are touched only through statements that join/filter on the parent vehicle's owner, and blobs are read/written/deleted only after that SQL ownership check; foreign-owner access returns 404. |
| Dependencies justified | PASS | See [research.md](research.md) R1, R2, R9, R12. |

**Post-design re-check (after Phase 1)**: unchanged; no violations. Open item for review: the Web feature pages are static-SSR (not interactive) because token acquisition needs `HttpContext` (feature 001 R4); the app-level render mode remains Interactive Server. Recorded under Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/002-vehicle-tracking/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── api.openapi.yaml
│   └── web-routes.md
├── checklists/
│   └── requirements.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
Directory.Packages.props               # + Dapper, Aspire.Npgsql, Aspire.Azure.Storage.Blobs, Aspire.Hosting.PostgreSQL, Aspire.Hosting.Azure.Storage, Testcontainers.PostgreSql, Testcontainers.Azurite
MyGarage.slnx                          # + MyGarage.Database, MyGarage.Database.Tests

src/
├── MyGarage.AppHost/
│   └── AppHost.cs                     # + AddPostgres("postgres").WithDataVolume().AddDatabase("garagedb"); AddAzureStorage("storage").RunAsEmulator(persistent) .AddBlobs("blobs"); api WithReference + WaitFor both
├── MyGarage.Database/                 # NEW. References no MyGarage project.
│   ├── Common/                        # SqlScriptStore, MigrationRunner, LikePattern (escape), PagedResult<T>, DI extension
│   ├── Vehicles/                      # VehicleRepository (CRUD, search/filter list, filter options), data records
│   ├── Tags/                          # VehicleTagRepository, VehicleTagRecord
│   ├── JournalNotes/                  # JournalNoteRepository, JournalNoteRecord
│   ├── VehicleImages/                 # VehicleImageRepository (rows + blobs), IVehicleImageBlobStore + AzureBlobVehicleImageStore, records
│   └── Scripts/                       # embedded .sql files, one subfolder per feature
│       ├── Common/                    # EnsureMigrationLedger.sql, ledger read/insert, advisory lock
│       ├── Migrations/                # 0001_create_vehicles.sql, 0002_create_vehicle_tags.sql,
│       │                              # 0003_create_journal_notes.sql, 0004_create_vehicle_images.sql
│       ├── Vehicles/                  # Insert, GetById, Update, Delete, ListPage (search + filters), FilterOptions ...
│       ├── Tags/
│       ├── JournalNotes/
│       └── VehicleImages/
├── MyGarage.Api/
│   ├── Program.cs                     # + AddNpgsqlDataSource("garagedb"), AddAzureBlobServiceClient("blobs"), AddGarageDatabase(), run migrations + ensure container at startup, MapVehiclesEndpoints()
│   ├── Common/                        # + CurrentOwner (claims -> owner id), paging validators, image signature sniffing
│   └── Vehicles/                      # VehiclesEndpoints (+ Tags/JournalNotes/Images endpoint groups), validators, mappers, services
├── MyGarage.Shared/
│   └── Vehicles/                      # VehicleSummaryDto, VehicleDetailDto, VehicleRequest, VehicleFilterOptionsDto, VehicleTagDto, AddTagRequest,
│                                      # JournalNoteDto, JournalNoteRequest, VehicleImageDto, PagedResultDto<T>
└── MyGarage.Web/
    ├── Common/                        # (existing) token handler, GarageApiClient; + Pager component, paging helpers
    ├── Vehicles/                      # VehicleList.razor (cards + search/filter form), VehicleCard.razor, VehicleFilters.razor, VehicleCreate.razor,
    │                                  # VehicleEdit.razor, VehicleDetail.razor (tags, journal, gallery), VehiclesApiClient, VehicleImageEndpoints
    └── Components/Layout/NavMenu.razor # + "My garage" link

tests/
├── MyGarage.Database.Tests/           # NEW: Common/ (Postgres + Azurite fixtures, migrations), Vehicles/ (incl. search/filter), Tags/, JournalNotes/, VehicleImages/
├── MyGarage.Api.Tests/                # + Vehicles/ (endpoints, validators, mappers, cross-user isolation); ApiFactory uses Testcontainers
├── MyGarage.Web.Tests/                # + Vehicles/ (cards, list paging, filter form, forms, image endpoint)
└── MyGarage.AppHost.Tests/            # smoke test also waits for the database and storage resources
```

**Structure Decision**: Extend the existing multi-project solution per constitution III. `MyGarage.Database` is created now (this is the first feature needing persistence). Feature sub-areas that have their own table and repository (tags, journal notes, images) get their own folders so each can be understood, changed, and removed in one place; the user-facing feature remains `Vehicles`.

## Complexity Tracking

| Deviation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Vehicle pages use static SSR (`[ExcludeFromInteractiveRouting]`) instead of Interactive Server | Calling the API with the user's access token requires `HttpContext`, which is not reliably available in interactive circuits (feature 001 R4). Search/filter is a GET form; forms, tags, and notes work as post-redirect-get form posts. | A circuit-safe token provider adds token-lifetime and security complexity and is not needed to meet the spec; it can be introduced later when a feature needs live interactivity. |
| Blob-backed image store lives in `MyGarage.Database` (adds the `Azure.Storage.Blobs` dependency there) | Constitution III defines `MyGarage.Database` as the persistence layer ("repositories, SQL scripts, and schema migrations") and the Api must not do data access itself. Keeping the row-plus-blob consistency logic in one repository keeps ownership checks and cleanup together. Reviewer approval is requested; a constitution PATCH clarifying that blob persistence belongs to `MyGarage.Database` is suggested. | A new `MyGarage.Storage` project is not permitted by III without an amendment and is not justified for one store; putting blob calls in `MyGarage.Api` would split one feature's persistence across two projects. |
