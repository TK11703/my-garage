---

description: "Task list for Vehicle Tracking"
---

# Tasks: Vehicle Tracking

**Input**: Design documents from `/specs/002-vehicle-tracking/`

**Prerequisites**: plan.md, spec.md, research.md (R1-R16), data-model.md, contracts/ (`api.openapi.yaml`, `web-routes.md`), quickstart.md

**Tests**: Included. The constitution (VIII) requires test-first development: write each story's tests, run them, and confirm they FAIL before implementing. Repository, migration, and API tests use real PostgreSQL and Azurite via Testcontainers (Docker required); the database is never mocked.

**Organization**: Tasks are grouped by user story. Paths follow the structure in plan.md. Story numbers match spec.md: US1 add and browse cards (P1), US2 search and filter (P2), US3 edit and remove (P2), US4 tags (P2), US5 journal notes (P2), US6 image gallery (P3).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1-US6, only on user-story phase tasks
- Constraints from data-model.md and research.md R13 are quoted in the task that creates the artifact; do not loosen them

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New projects and packages; the solution still builds with zero warnings.

- [X] T001 Update `Directory.Packages.props`: add pinned `PackageVersion` entries (current stable, Aspire entries matching the existing 13.x pin) for `Dapper`, `Npgsql`, `Azure.Storage.Blobs`, `Aspire.Npgsql`, `Aspire.Azure.Storage.Blobs`, `Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.Azure.Storage`, `Testcontainers.PostgreSql`, `Testcontainers.Azurite`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`; keep the existing transitive pins
- [X] T002 Create class library `src/MyGarage.Database/MyGarage.Database.csproj` with NO `ProjectReference` to any MyGarage project; `PackageReference` Dapper, Npgsql, Azure.Storage.Blobs, and the three `Microsoft.Extensions.*` abstractions; embed SQL with `<EmbeddedResource Include="Scripts\**\*.sql" />`; add `[assembly: InternalsVisibleTo("MyGarage.Database.Tests")]` in `src/MyGarage.Database/AssemblyInfo.cs`
- [X] T003 [P] Create xUnit project `tests/MyGarage.Database.Tests/MyGarage.Database.Tests.csproj` (references `MyGarage.Database`; `PackageReference` xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, coverlet.collector, Testcontainers.PostgreSql, Testcontainers.Azurite)
- [X] T004 Wire references and solution: add `src/MyGarage.Database` and `tests/MyGarage.Database.Tests` to `MyGarage.slnx` (solution folders `src`, `tests`); add `ProjectReference` Api -> Database and `PackageReference` Aspire.Npgsql in `src/MyGarage.Api/MyGarage.Api.csproj`; add `PackageReference` Testcontainers.PostgreSql and Testcontainers.Azurite to `tests/MyGarage.Api.Tests/MyGarage.Api.Tests.csproj`; add `PackageReference` Aspire.Hosting.PostgreSQL to `src/MyGarage.AppHost/MyGarage.AppHost.csproj`; verify `dotnet build MyGarage.slnx` has zero warnings

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Database infrastructure, complete schema, shared DTOs, and test fixtures that every story depends on. The full schema lives here (not per story) because search (US2) spans tags, and delete (US3) cascades to tags, notes, and images.

**CRITICAL**: No user story work begins until this phase is complete.

### Tests first (confirm they fail)

- [X] T005 [P] Create fixtures in `tests/MyGarage.Database.Tests/Common/PostgresFixture.cs` (Testcontainers `PostgreSqlContainer`, exposes `NpgsqlDataSource`, creates a fresh database per call via `CreateDatabaseAsync()`) and `tests/MyGarage.Database.Tests/Common/DatabaseCollection.cs` (xUnit collection definition sharing the container)
- [X] T006 [P] `SqlScriptStoreTests` in `tests/MyGarage.Database.Tests/Common/SqlScriptStoreTests.cs`: returns the text of an embedded script by `"<Feature>/<Name>"`; the second call returns the cached instance; unknown name throws `InvalidOperationException` naming the script; every embedded resource under `Scripts/` is a `.sql` file and none contains string-interpolation placeholders
- [X] T007 [P] `MigrationRunnerTests` in `tests/MyGarage.Database.Tests/Common/MigrationRunnerTests.cs` against a fresh database: applies 0001-0004 to an empty database and creates tables `vehicles`, `vehicle_tags`, `journal_notes`, `vehicle_images`, `schema_migrations`; ledger has one row per script with `script_id`, SHA-256 `checksum`, `applied_at`; a second run applies nothing; a changed checksum (update the ledger row) makes the runner throw naming the script; two runners started concurrently both succeed and apply each script once (advisory lock); schema assertions: `mileage` CHECK rejects -1 and 10000000, `doors` CHECK rejects 0 and 7, `size_bytes` CHECK rejects 0 and 5242881, duplicate `(vehicle_id, name_key)` rejected, second `is_main` row for one vehicle rejected, deleting a vehicle cascades to tags, notes, and images
- [X] T008 [P] `LikePatternTests` in `tests/MyGarage.Database.Tests/Common/LikePatternTests.cs`: `Contains("50%")` -> `%50\%%`, `Contains("a_b")` -> `%a\_b%`, backslash is doubled, quotes and angle brackets are unchanged, null/whitespace-only input -> `null`, input is trimmed

### Implementation

- [X] T009 [P] Implement `src/MyGarage.Database/Common/SqlScriptStore.cs`: singleton that reads embedded resources named `MyGarage.Database.Scripts.<Feature>.<Name>.sql` once, caches in a `ConcurrentDictionary`, and throws a clear error for unknown names; no SQL literals in C# anywhere in the project
- [X] T010 [P] Implement `src/MyGarage.Database/Common/LikePattern.cs` (`Contains(string?)`: trim, return `null` when empty, escape `\`, `%`, `_` with `\`, wrap in `%`) and `src/MyGarage.Database/Common/PagedResult.cs` (`PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)` with `TotalPages`)
- [X] T011 [P] Create `src/MyGarage.Database/Scripts/Migrations/0001_create_vehicles.sql`: table `vehicles` with `id` uuid PRIMARY KEY; `owner_id` text NOT NULL; `nickname` varchar(100) NOT NULL; `make` varchar(50) NOT NULL; `model` varchar(50) NOT NULL; `trim` varchar(50) NULL; `color` varchar(30) NULL; `mileage` integer NULL with `CHECK (mileage BETWEEN 0 AND 9999999)`; `doors` smallint NULL with `CHECK (doors BETWEEN 1 AND 6)`; `created_at` and `updated_at` timestamptz NOT NULL; `created_by` and `updated_by` text NOT NULL; index `ix_vehicles_owner_nickname (owner_id, lower(nickname), id)`
- [X] T012 [P] Create `src/MyGarage.Database/Scripts/Migrations/0002_create_vehicle_tags.sql`: table `vehicle_tags` with `id` uuid PRIMARY KEY; `vehicle_id` uuid NOT NULL REFERENCES `vehicles(id)` ON DELETE CASCADE; `name` varchar(30) NOT NULL; `name_key` text NOT NULL generated always as `lower(name)` stored; `created_at` timestamptz NOT NULL; `UNIQUE (vehicle_id, name_key)`
- [X] T013 [P] Create `src/MyGarage.Database/Scripts/Migrations/0003_create_journal_notes.sql`: table `journal_notes` with `id` uuid PRIMARY KEY; `vehicle_id` uuid NOT NULL REFERENCES `vehicles(id)` ON DELETE CASCADE; `category` varchar(40) NULL (free text, no predefined list, no CHECK); `body` varchar(4000) NOT NULL; `created_at` and `updated_at` timestamptz NOT NULL; `created_by` and `updated_by` text NOT NULL; index `ix_journal_notes_vehicle_created (vehicle_id, created_at DESC, id DESC)`
- [X] T014 [P] Create `src/MyGarage.Database/Scripts/Migrations/0004_create_vehicle_images.sql`: table `vehicle_images` (metadata only, no content column) with `id` uuid PRIMARY KEY; `vehicle_id` uuid NOT NULL REFERENCES `vehicles(id)` ON DELETE CASCADE; `content_type` text NOT NULL; `size_bytes` integer NOT NULL with `CHECK (size_bytes BETWEEN 1 AND 5242880)`; `caption` varchar(200) NULL; `is_main` boolean NOT NULL DEFAULT false; `created_at` timestamptz NOT NULL; `created_by` text NOT NULL; index `ix_vehicle_images_vehicle (vehicle_id, created_at, id)`; partial unique index `ux_vehicle_images_main (vehicle_id) WHERE is_main`
- [X] T015 Create `src/MyGarage.Database/Scripts/Common/` scripts `EnsureMigrationLedger.sql` (`schema_migrations(script_id text PRIMARY KEY, checksum text NOT NULL, applied_at timestamptz NOT NULL)`), `GetAppliedMigrations.sql`, `RecordMigration.sql`, `AcquireMigrationLock.sql` (`pg_advisory_lock` with a fixed key); then implement `src/MyGarage.Database/Common/MigrationRunner.cs` per research R3 (advisory lock, ledger bootstrap, SHA-256 checksum verification that fails on drift, each pending script and its ledger insert in one transaction, ordered by script id) so T007 passes
- [X] T016 Implement `src/MyGarage.Database/Common/DatabaseServiceCollectionExtensions.cs` (`AddGarageDatabase(this IServiceCollection)`: registers `SqlScriptStore`, `MigrationRunner`, and the repositories added by later phases; sets `Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true`) and `src/MyGarage.Database/Common/MigrationHostedService.cs` (`IHostedService` that runs `MigrationRunner` in `StartAsync` before the host accepts requests; logs script ids only)
- [X] T017 [P] Create all shared DTOs in `src/MyGarage.Shared/Vehicles/` (one file each, no owner/user id fields, per data-model.md): `PagedResultDto<T>` (`Items`, `Page`, `PageSize`, `TotalCount`, `TotalPages`), `VehicleSummaryDto` (`Id`, `Nickname`, `Make`, `Model`, `Mileage?`, `MainImageId?`), `VehicleDetailDto` (`Id`, `Nickname`, `Make`, `Model`, `Trim?`, `Color?`, `Mileage?`, `Doors?`, `Tags`, `Images`, `CreatedAt`, `UpdatedAt`), `VehicleRequest` (`Nickname`, `Make`, `Model`, `Trim?`, `Color?`, `Mileage?`, `Doors?`), `VehicleTagDto` (`Id`, `Name`), `AddVehicleTagRequest` (`Name`), `JournalNoteDto` (`Id`, `Category?`, `Body`, `CreatedAt`, `UpdatedAt`), `JournalNoteRequest` (`Body`, `Category?` string), `VehicleImageDto` (`Id`, `Caption?`, `IsMain`, `ContentType`, `SizeBytes`, `CreatedAt`), `VehicleFilterOptionsDto` (`Makes`, `Colors`, `Tags` string arrays)
- [X] T018 [P] Implement `src/MyGarage.Api/Common/CurrentOwner.cs` (reads the owner id from the validated `ClaimsPrincipal` using the same claim precedence as `CurrentUserMapper`: `oid`, objectidentifier URI, `sub`, `NameIdentifier`; returns `null` when absent so callers answer 403) with `tests/MyGarage.Api.Tests/Common/CurrentOwnerTests.cs` written first (oid preferred over sub; sub fallback; no claim -> null; a `userId`/`ownerId` value placed in claims named otherwise is ignored)
- [X] T019 [P] Implement `src/MyGarage.Api/Common/PagingQuery.cs` (record `Page` default 1, `PageSize` default supplied by caller) and `PagingQueryValidator` in the same folder: `page >= 1`, `pageSize` 1-50, else FluentValidation errors; unit tests in `tests/MyGarage.Api.Tests/Common/PagingQueryValidatorTests.cs` written first
- [X] T020 Wire the Api in `src/MyGarage.Api/Program.cs`: `builder.AddNpgsqlDataSource("garagedb")`, `builder.Services.AddGarageDatabase()`, register `MigrationHostedService`; keep existing registrations
- [X] T021 Update the AppHost in `src/MyGarage.AppHost/AppHost.cs`: `builder.AddPostgres("postgres").WithDataVolume()` then `.AddDatabase("garagedb")`; `api` gets `.WithReference(garageDb).WaitFor(garageDb)`; `web` is unchanged
- [X] T022 Update `tests/MyGarage.Api.Tests/Common/ApiFactory.cs` to start a Testcontainers PostgreSQL container (shared via an xUnit collection fixture in `tests/MyGarage.Api.Tests/Common/ApiCollection.cs`) and set `ConnectionStrings:garagedb` in its configuration; add `CreateClientForUser(string oid)` using `TestJwt.Create(claims: { ["oid"] = oid })` and a `ResetDataAsync()` that truncates `vehicles` cascade between tests; confirm the existing Users and SampleData endpoint tests still pass
- [X] T023 [P] Update `tests/MyGarage.AppHost.Tests/AppHostSmokeTests.cs` so `ApiAndWeb_StartAndReportHealthy` also waits for `postgres` and `garagedb` to be healthy (Docker required)

**Checkpoint**: `dotnet test` passes for foundational tests (T005-T008, T018, T019, existing tests); migrations apply to an empty database; Aspire starts with PostgreSQL.

---

## Phase 3: User Story 1 - Add vehicles and browse them as cards (Priority: P1) MVP

**Goal**: A signed-in user sees their own vehicles as a paged card grid (picture or placeholder, nickname, make, model, mileage), can add a vehicle with validation, and can open its detail page.

**Independent Test**: Sign in as a user with no vehicles (empty state), add a vehicle (details shown on its page and as a card), add 13+ vehicles and page through the cards; a second user sees none of them.

### Tests for User Story 1 (write first, confirm they fail)

- [X] T024 [P] [US1] `VehicleRepositoryTests` in `tests/MyGarage.Database.Tests/Vehicles/VehicleRepositoryTests.cs`: insert then get round-trips every column (including nulls) with UTC `created_at`/`updated_at` and `created_by`/`updated_by` = owner; get by another owner returns `null`; list returns only the owner's rows ordered by `lower(nickname), id` (two equal nicknames keep a stable order across pages); paging returns correct slices and `TotalCount`; page beyond the last returns an empty slice from the repository (clamping is done by the service, T032)
- [X] T025 [P] [US1] `VehicleRequestValidatorTests` in `tests/MyGarage.Api.Tests/Vehicles/VehicleRequestValidatorTests.cs`: `nickname` required 1-100 chars (trimmed); `make` and `model` required 1-50; `trim` optional <= 50; `color` optional <= 30; `mileage` optional integer 0-9,999,999; `doors` optional integer 1-6; control characters rejected in all text fields; each boundary (0, 9999999, -1, 10000000, 1, 6, 0, 7) is covered
- [X] T026 [P] [US1] `VehicleMapperTests` in `tests/MyGarage.Api.Tests/Vehicles/VehicleMapperTests.cs`: explicit mapping from `VehicleRecord`/`VehicleSummaryRecord` to `VehicleDetailDto`/`VehicleSummaryDto` (every field, `MainImageId`, empty `Tags`/`Images` until US4/US6) and from `VehicleRequest` to the repository's create arguments with values trimmed; no owner id appears on any DTO
- [X] T027 [P] [US1] `VehiclesEndpointsTests` in `tests/MyGarage.Api.Tests/Vehicles/VehiclesEndpointsTests.cs` (create, list, get): `POST /api/vehicles` 201 with `Location` and `VehicleDetailDto`; 400 `ValidationProblem` for blank nickname, mileage -5, doors 9; `GET /api/vehicles` first page of 12 with `TotalCount`/`TotalPages`; page 999 returns the last page with its real `page` number; `page=0` or `pageSize=51` -> 400; `GET /api/vehicles/{id}` 200 and 404 `ProblemDetails` for an unknown id; no token -> 401; a request body or query containing `ownerId`/`userId` is ignored; cross-user isolation: user B's list is empty and user B's `GET` of user A's vehicle is 404
- [X] T028 [P] [US1] bUnit tests in `tests/MyGarage.Web.Tests/Vehicles/VehicleCardTests.cs` (card renders nickname as a link, `make model`, formatted mileage or "Mileage not set", placeholder when `MainImageId` is null, `<img>` with `alt` = nickname and `loading="lazy"` when present, no `<table>` markup) and `tests/MyGarage.Web.Tests/Vehicles/VehicleListTests.cs` (empty-garage state with "Add vehicle" link, card grid, pager shows "Page X of Y", previous/next links, first/last disabled states, out-of-range query value shown as the page the API returned)
- [X] T029 [P] [US1] `VehicleCreatePageTests` in `tests/MyGarage.Web.Tests/Vehicles/VehicleCreatePageTests.cs`: labeled inputs for nickname, make, model, trim, color, mileage, doors; API 400 validation errors are shown per field and nothing is lost from the form; success redirects to the new vehicle's page; API 401/403 shows the existing "sign in again" message

### Implementation for User Story 1

- [X] T030 [P] [US1] Create data records in `src/MyGarage.Database/Vehicles/` : `VehicleRecord` (all `vehicles` columns), `VehicleSummaryRecord` (`Id`, `Nickname`, `Make`, `Model`, `Mileage`, `MainImageId`)
- [X] T031 [US1] Create `src/MyGarage.Database/Scripts/Vehicles/Insert.sql`, `GetById.sql`, `ListPage.sql`, `Count.sql` (every statement has `WHERE ... owner_id = @OwnerId`; `ListPage` orders by `lower(v.nickname), v.id` with `LIMIT @PageSize OFFSET @Offset` and `LEFT JOIN vehicle_images mi ON mi.vehicle_id = v.id AND mi.is_main` selecting only `mi.id` as `main_image_id`); implement `src/MyGarage.Database/Vehicles/VehicleRepository.cs` (`InsertAsync`, `GetByIdAsync(ownerId, id)`, `ListPageAsync(ownerId, page, pageSize)`; all take `CancellationToken` and run through `SqlScriptStore` and Dapper `CommandDefinition`); register in `AddGarageDatabase` (T016) so T024 passes
- [X] T032 [P] [US1] Implement `src/MyGarage.Api/Vehicles/VehicleRequestValidator.cs` (rules exactly as in T025), `src/MyGarage.Api/Vehicles/VehicleMapper.cs` (explicit mapping, empty `Tags`/`Images` placeholders), and `src/MyGarage.Api/Vehicles/VehicleService.cs` (create with `Guid.CreateVersion7()` ids and UTC `DateTimeOffset.UtcNow`; `ListAsync` computes `TotalPages` and clamps `page` to the last valid page, re-querying when needed; `GetAsync`)
- [X] T033 [US1] Implement `src/MyGarage.Api/Vehicles/VehiclesEndpoints.cs`: `MapVehiclesEndpoints()` with `MapGroup("/api/vehicles").WithTags("Vehicles")`; `GET /` (`[AsParameters]` paging query validated by `PagingQueryValidator`, default `pageSize` 12), `POST /` (`ValidationFilter<VehicleRequest>`, 201 + `Location`), `GET /{vehicleId:guid}`; owner from `CurrentOwner` (403 `ProblemDetails` when absent); 404 `ProblemDetails` when not found; `Produces`/`ProducesProblem` for 200/201/400/401/403/404 and `WithName` matching `api.openapi.yaml` operationIds; register validators and service in DI and call `app.MapVehiclesEndpoints()` in `src/MyGarage.Api/Program.cs` so T025-T027 pass
- [X] T034 [P] [US1] Create `src/MyGarage.Web/Vehicles/VehiclesApiClient.cs` (typed `HttpClient`: `ListAsync(page, pageSize, ct)`, `CreateAsync`, `GetAsync`; maps 401/403 -> `GarageApiException(NotAccepted)`, 404 -> a `VehicleNotFound` result, 400 -> a result carrying the `errors` dictionary) and register it in `src/MyGarage.Web/Program.cs` with `AddHttpClient<VehiclesApiClient>(client => client.BaseAddress = new Uri("https+http://api")).AddHttpMessageHandler<ApiAccessTokenHandler>()`
- [X] T035 [P] [US1] Create `src/MyGarage.Web/Common/Pager.razor` (Bootstrap `pagination` inside `<nav aria-label="Pagination">`, previous/next links built from a base URL with the current query string preserved, "Page X of Y", disabled states with `aria-disabled`)
- [X] T036 [P] [US1] Create `src/MyGarage.Web/Vehicles/VehicleCard.razor` (Bootstrap `card` with `card-img-top` or placeholder block, title link to `/vehicles/{id}`, `make model`, mileage with thousands separator and "mi"; image source `/vehicles/{vehicleId}/images/{imageId}` added in US6 wiring; encoded text only, no `MarkupString`)
- [X] T037 [US1] Create `src/MyGarage.Web/Vehicles/VehicleList.razor` (`@page "/vehicles"`, `[Authorize]`, `[ExcludeFromInteractiveRouting]`, `[SupplyParameterFromQuery] int? Page`; Bootstrap `row row-cols-1 row-cols-sm-2 row-cols-lg-3 g-3` grid of `VehicleCard`; empty-garage state; `Pager`; failure alert like `ApiCheck.razor`)
- [X] T038 [US1] Create `src/MyGarage.Web/Vehicles/VehicleCreate.razor` (`@page "/vehicles/new"`, static SSR, `EditForm FormName="create-vehicle"` with antiforgery, labeled inputs, per-field validation messages from API errors, redirect to `/vehicles/{id}` on success) and `src/MyGarage.Web/Vehicles/VehicleDetail.razor` (`@page "/vehicles/{VehicleId:guid}"`, shows all vehicle details, "Vehicle not found" state via the standard not-found page, links to the garage; sections for tags, journal, and images are added by US4-US6)
- [X] T039 [P] [US1] Add a "My garage" `NavLink` to `src/MyGarage.Web/Components/Layout/NavMenu.razor` and a link to `/vehicles` on `src/MyGarage.Web/Home/Home.razor`; confirm the existing `LoginDisplayTests` and `HomeTests` still pass

**Checkpoint**: US1 tests pass; quickstart steps 2.1-2.3 and section 3 (isolation) work.

---

## Phase 4: User Story 2 - Search and filter the garage (Priority: P2)

**Goal**: The garage page narrows cards by text search and filters (make, color, doors, tag, min/max mileage), keeps criteria through paging, and clears them in one action.

**Independent Test**: With varied vehicles and tags, apply search text and each filter and confirm only matching vehicles appear with a correct count; clear restores everything; `%` and `_` match literally; another user never appears.

### Tests for User Story 2 (write first, confirm they fail)

- [X] T040 [P] [US2] `VehicleSearchTests` in `tests/MyGarage.Database.Tests/Vehicles/VehicleSearchTests.cs`: `q` matches (case-insensitively, as contains) nickname, make, model, trim, color, and tag names; `q` of `%` and `_` match only literal characters; `make`/`color` exact ignoring case; `doors` equality; `tag` requires that tag (case-insensitive); `minMileage`/`maxMileage` inclusive and vehicles with null mileage never match a mileage bound; all criteria combine with AND; `TotalCount` equals the filtered total; paging uses the filtered set; another owner's matching vehicles are never returned; `GetFilterOptionsAsync` returns distinct, case-insensitively de-duplicated, sorted makes, colors, and tag names for only the caller's garage
- [X] T041 [P] [US2] `VehicleListQueryValidatorTests` in `tests/MyGarage.Api.Tests/Vehicles/VehicleListQueryValidatorTests.cs`: `q` optional, trimmed, <= 100 chars (whitespace-only treated as absent); `make` <= 50; `color` <= 30; `tag` <= 30; `doors` 1-6; `minMileage` and `maxMileage` 0-9,999,999 and `minMileage <= maxMileage`; paging rules from T019
- [X] T042 [P] [US2] Extend `tests/MyGarage.Api.Tests/Vehicles/VehiclesEndpointsTests.cs` (or add `VehicleSearchEndpointsTests.cs` in the same folder): `GET /api/vehicles` with each criterion and combinations returns only matches with correct `totalCount`/`totalPages`; clamped page after narrowing; invalid criteria (min > max, `doors=9`, `q` of 101 chars) -> 400 `ValidationProblem`; `GET /api/vehicles/filter-options` 200 with the caller's values only, 401 without a token, and user B sees nothing of user A's values; `/api/vehicles/filter-options` is not captured by `/{vehicleId:guid}`
- [X] T043 [P] [US2] `VehicleFiltersTests` in `tests/MyGarage.Web.Tests/Vehicles/VehicleFiltersTests.cs`: search box and collapsible filter panel (open when any filter is active); selects populated from filter options; active criteria rendered as badges that link to the same URL without that criterion; "Clear all" link; "No vehicles match" state (criteria present, zero results) distinct from the empty-garage state; pager links keep all criteria; min > max shows an inline message and the page still renders; result summary in a polite live region

### Implementation for User Story 2

- [X] T044 [US2] Add `VehicleSearchCriteria` and `VehicleFilterOptionsRecord` in `src/MyGarage.Database/Vehicles/`; extend `src/MyGarage.Database/Scripts/Vehicles/ListPage.sql` and `Count.sql` with optional parameter predicates (`@Pattern IS NULL OR v.nickname ILIKE @Pattern ESCAPE '\' OR v.make ILIKE ... OR v.model ... OR v.trim ... OR v.color ... OR EXISTS (SELECT 1 FROM vehicle_tags t WHERE t.vehicle_id = v.id AND t.name ILIKE @Pattern ESCAPE '\')`; `@Make IS NULL OR lower(v.make) = lower(@Make)`; same for `@Color`; `@Doors IS NULL OR v.doors = @Doors`; `@TagKey IS NULL OR EXISTS (SELECT 1 FROM vehicle_tags t WHERE t.vehicle_id = v.id AND t.name_key = @TagKey)`; `@MinMileage IS NULL OR v.mileage >= @MinMileage`; `@MaxMileage IS NULL OR v.mileage <= @MaxMileage`), always with `v.owner_id = @OwnerId`; add `src/MyGarage.Database/Scripts/Vehicles/FilterOptions.sql` (three owner-scoped distinct queries for make, color, tag name); update `VehicleRepository.ListPageAsync` to take `VehicleSearchCriteria` (build the pattern with `LikePattern.Contains`, lower-case the tag key) and add `GetFilterOptionsAsync(ownerId, ct)` so T040 passes and T024 still passes
- [X] T045 [US2] Implement `src/MyGarage.Api/Vehicles/VehicleListQuery.cs` (`[AsParameters]` record: `q`, `make`, `color`, `doors`, `tag`, `minMileage`, `maxMileage`, `page`, `pageSize`) and `VehicleListQueryValidator.cs` (rules from T041, reuse `PagingQueryValidator` rules); update `VehicleService.ListAsync` and the `GET /api/vehicles` handler in `VehiclesEndpoints.cs` to pass criteria and run the query validator through an endpoint filter; add `GET /api/vehicles/filter-options` (`WithName("GetVehicleFilterOptions")`, returns `VehicleFilterOptionsDto`, 401/403) declared before `/{vehicleId:guid}`; log counts only, never criteria values
- [X] T046 [US2] Extend `src/MyGarage.Web/Vehicles/VehiclesApiClient.cs` with a `VehicleListCriteria` record (query-string builder that omits empty values and URL-encodes) used by `ListAsync`, and `GetFilterOptionsAsync`; surface API 400 errors as a result so the page can show them
- [X] T047 [P] [US2] Create `src/MyGarage.Web/Vehicles/VehicleFilters.razor` (GET `<form method="get">` with a labeled search input and Bootstrap collapse panel of make, color, doors (1-6), tag selects and min/max mileage number inputs; "Apply" and "Clear all"; active-criteria badges with remove links; accessible names on all controls and badge links)
- [X] T048 [US2] Update `src/MyGarage.Web/Vehicles/VehicleList.razor` to bind all criteria with `[SupplyParameterFromQuery]`, load filter options, render `VehicleFilters`, show "No vehicles match" vs the empty-garage state, a result summary in `aria-live="polite"`, pass criteria to `Pager` so page links keep them, and show API validation messages without an error page

**Checkpoint**: US2 tests pass; quickstart step 2.4 works.

---

## Phase 5: User Story 3 - Edit and remove a vehicle (Priority: P2)

**Goal**: A user can update any vehicle detail (invalid values rejected, stored data unchanged) and remove a vehicle after confirmation, with everything attached removed.

**Independent Test**: Edit a vehicle and see it on the detail page and card; delete after confirming (cancel keeps it); a second user cannot edit or delete it (404).

### Tests for User Story 3 (write first, confirm they fail)

- [X] T049 [P] [US3] Extend `tests/MyGarage.Database.Tests/Vehicles/VehicleRepositoryTests.cs`: update replaces all editable fields, sets `updated_at`/`updated_by`, leaves `created_*` unchanged, returns `false`/null for another owner and for an unknown id; delete removes the vehicle and cascades to tags, notes, and image rows, returns `false` for another owner (the other owner's vehicle still exists)
- [X] T050 [P] [US3] Extend `tests/MyGarage.Api.Tests/Vehicles/VehiclesEndpointsTests.cs`: `PUT /api/vehicles/{id}` 200 returns the updated `VehicleDetailDto`; 400 for invalid values with stored data unchanged; 404 for unknown id and for another user's vehicle; 401 without token; `DELETE /api/vehicles/{id}` 204 then 404 on get; 404 for another user's vehicle (vehicle remains); 401 without token
- [X] T051 [P] [US3] `VehicleEditPageTests` in `tests/MyGarage.Web.Tests/Vehicles/VehicleEditPageTests.cs` (form prefilled, per-field API errors shown, cancel link returns to detail, success redirects to detail) and `tests/MyGarage.Web.Tests/Vehicles/VehicleDeleteTests.cs` (confirmation modal text, cancel leaves the vehicle, confirm posts `delete-vehicle` and redirects to `/vehicles`)

### Implementation for User Story 3

- [X] T052 [US3] Add `src/MyGarage.Database/Scripts/Vehicles/Update.sql` and `Delete.sql` (both `WHERE id = @Id AND owner_id = @OwnerId`; update sets `updated_at`, `updated_by`) and `UpdateAsync`/`DeleteAsync` on `VehicleRepository` returning `bool`, so T049 passes
- [X] T053 [US3] Add `UpdateAsync`/`DeleteAsync` to `src/MyGarage.Api/Vehicles/VehicleService.cs` and `PUT /{vehicleId:guid}` (`ValidationFilter<VehicleRequest>`, 200/400/401/403/404) and `DELETE /{vehicleId:guid}` (204/401/403/404) to `src/MyGarage.Api/Vehicles/VehiclesEndpoints.cs` with `WithName("UpdateVehicle")`/`WithName("DeleteVehicle")`, so T050 passes
- [X] T054 [P] [US3] Extend `src/MyGarage.Web/Vehicles/VehiclesApiClient.cs` with `UpdateAsync` and `DeleteAsync`; create `src/MyGarage.Web/Vehicles/VehicleEdit.razor` (`@page "/vehicles/{VehicleId:guid}/edit"`, static SSR, `EditForm FormName="update-vehicle"`, same fields and messages as create, cancel link)
- [X] T055 [US3] Add Edit link and a Delete button to `src/MyGarage.Web/Vehicles/VehicleDetail.razor` with a Bootstrap modal (data attributes, no custom JavaScript) containing a `delete-vehicle` form; redirect to `/vehicles` after delete

**Checkpoint**: US3 tests pass; quickstart step 2.5 works.

---

## Phase 6: User Story 4 - Describe a vehicle with free-text tags (Priority: P2)

**Goal**: Add and remove free-text tags on a vehicle (unique ignoring case, max 20, 1-30 characters).

**Independent Test**: Add a tag, see it on the vehicle, add a case/space variant (duplicate message), remove it.

### Tests for User Story 4 (write first, confirm they fail)

- [X] T056 [P] [US4] `VehicleTagRepositoryTests` in `tests/MyGarage.Database.Tests/Tags/VehicleTagRepositoryTests.cs`: add returns the tag; `"Sunroof"` then `"sunroof"` -> duplicate outcome; the 21st tag -> limit outcome; concurrent adds of the 20th tag cannot exceed 20 (row lock); another owner's vehicle -> not-found outcome and nothing inserted; remove deletes only that tag; remove with another owner's ids -> not found; `ListByVehicle` returns tags ordered by `created_at, id` and only for the owner's vehicle
- [X] T057 [P] [US4] `AddVehicleTagRequestValidatorTests` in `tests/MyGarage.Api.Tests/Vehicles/Tags/AddVehicleTagRequestValidatorTests.cs`: `name` required, trimmed, 1-30 characters, no control characters; whitespace-only rejected
- [X] T058 [P] [US4] `VehicleTagsEndpointsTests` in `tests/MyGarage.Api.Tests/Vehicles/Tags/VehicleTagsEndpointsTests.cs`: `POST /api/vehicles/{id}/tags` 201; 400 invalid; 409 `ProblemDetails` for duplicate (`" SUNROOF "` after `"sunroof"`) and for the 21st tag; 404 unknown vehicle and other user's vehicle; `DELETE .../tags/{tagId}` 204/404 (including cross-user); 401 without token; `GET /api/vehicles/{id}` now lists the tags
- [X] T059 [P] [US4] `VehicleTagsSectionTests` in `tests/MyGarage.Web.Tests/Vehicles/VehicleTagsSectionTests.cs`: tags shown as badges with encoded text; add form (labeled input); remove button with accessible name `Remove tag <name>`; duplicate and limit messages shown; invalid input message shown

### Implementation for User Story 4

- [X] T060 [P] [US4] Create `src/MyGarage.Database/Tags/VehicleTagRecord.cs` and scripts `src/MyGarage.Database/Scripts/Tags/LockVehicle.sql` (`SELECT id FROM vehicles WHERE id = @VehicleId AND owner_id = @OwnerId FOR UPDATE`), `CountForVehicle.sql`, `Insert.sql`, `Delete.sql` (`... WHERE id = @TagId AND vehicle_id = @VehicleId AND EXISTS (SELECT 1 FROM vehicles WHERE id = @VehicleId AND owner_id = @OwnerId)`), `ListByVehicle.sql` (owner-scoped through the parent vehicle)
- [X] T061 [US4] Implement `src/MyGarage.Database/Tags/VehicleTagRepository.cs` (`AddAsync` in one transaction: lock parent row -> `NotFound` if absent, count -> `LimitReached` at 20, insert catching the unique violation -> `Duplicate`; `RemoveAsync`; `ListAsync`) returning a small result enum/record, and register it in `AddGarageDatabase`, so T056 passes
- [X] T062 [P] [US4] Implement `src/MyGarage.Api/Vehicles/Tags/AddVehicleTagRequestValidator.cs`, `VehicleTagMapper.cs`, and `VehicleTagsEndpoints.cs` (`MapGroup("/api/vehicles/{vehicleId:guid}/tags")`: `POST` 201/400/401/403/404/409 and `DELETE /{tagId:guid}` 204/401/403/404, `WithName("AddVehicleTag")`/`WithName("RemoveVehicleTag")`; the name is trimmed before the repository call; log ids only); register in `Program.cs`
- [X] T063 [US4] Update `src/MyGarage.Api/Vehicles/VehicleService.cs` and `VehicleMapper.cs` so `GetAsync`/create/update populate `VehicleDetailDto.Tags` from `VehicleTagRepository`
- [X] T064 [P] [US4] Extend `src/MyGarage.Web/Vehicles/VehiclesApiClient.cs` with `AddTagAsync`/`RemoveTagAsync` (409 surfaced as a conflict result with the problem title) and create `src/MyGarage.Web/Vehicles/VehicleTagsSection.razor` (forms `add-tag` and `remove-tag-{tagId}`, antiforgery, messages); include it in `VehicleDetail.razor`

**Checkpoint**: US4 tests pass; quickstart step 2.6 works; US2 tag search and tag filter now match real tags.

---

## Phase 7: User Story 5 - Keep a journal of notes about a vehicle (Priority: P2)

**Goal**: Add, list (newest first, paged), edit, and delete journal notes with an optional free-text category.

**Independent Test**: Add notes with made-up categories and one blank, confirm order, badge, date, paging, edit, and delete; another user cannot touch them.

### Tests for User Story 5 (write first, confirm they fail)

- [X] T065 [P] [US5] `JournalNoteRepositoryTests` in `tests/MyGarage.Database.Tests/JournalNotes/JournalNoteRepositoryTests.cs`: insert then list newest first by `created_at DESC, id DESC` with correct paging/`TotalCount`; category stored as entered (case kept) and `null` when blank; update changes body/category and `updated_at`/`updated_by` but not `created_*`; delete removes only that note; every operation through another owner's vehicle returns not found and changes nothing; notes of vehicle A never appear for vehicle B
- [X] T066 [P] [US5] `JournalNoteRequestValidatorTests` in `tests/MyGarage.Api.Tests/Vehicles/JournalNotes/JournalNoteRequestValidatorTests.cs`: `body` required, trimmed, 1-4,000 characters (boundaries 1, 4000, 4001); `category` optional, trimmed, <= 40 characters (40 ok, 41 rejected), no control characters, whitespace-only accepted as "none"
- [X] T067 [P] [US5] `JournalNotesEndpointsTests` in `tests/MyGarage.Api.Tests/Vehicles/JournalNotes/JournalNotesEndpointsTests.cs`: `GET /api/vehicles/{id}/notes` first page (default size 10) newest first, out-of-range page clamps, `pageSize=51` -> 400; `POST` 201 with free-text category and with blank category (`category` null in response); 400 invalid; `PUT /{noteId}` 200/400/404; `DELETE` 204/404; unknown or other user's vehicle -> 404 for all; 401 without token; angle brackets and quotes in body/category are returned unchanged
- [X] T068 [P] [US5] `VehicleJournalSectionTests` in `tests/MyGarage.Web.Tests/Vehicles/VehicleJournalSectionTests.cs`: add form with labeled body textarea and optional category text input (max length 40); notes listed newest first with category badge (none when null) and date; body and category rendered HTML-encoded; notes pager uses `notesPage` and keeps other query values; edit and delete (confirmation) forms; validation messages shown

### Implementation for User Story 5

- [X] T069 [P] [US5] Create `src/MyGarage.Database/JournalNotes/JournalNoteRecord.cs` and scripts `src/MyGarage.Database/Scripts/JournalNotes/Insert.sql`, `ListPage.sql`, `Count.sql`, `Update.sql`, `Delete.sql` (all owner-scoped through the parent vehicle with `EXISTS (SELECT 1 FROM vehicles WHERE id = @VehicleId AND owner_id = @OwnerId)`; list ordered `created_at DESC, id DESC`)
- [X] T070 [US5] Implement `src/MyGarage.Database/JournalNotes/JournalNoteRepository.cs` (`AddAsync`, `ListPageAsync`, `UpdateAsync`, `DeleteAsync`; vehicle-not-found distinguished from note-not-found where needed) and register it in `AddGarageDatabase`, so T065 passes
- [X] T071 [P] [US5] Implement `src/MyGarage.Api/Vehicles/JournalNotes/JournalNoteRequestValidator.cs`, `JournalNoteMapper.cs` (blank category -> `null`, trimmed), and `JournalNotesEndpoints.cs` (`MapGroup("/api/vehicles/{vehicleId:guid}/notes")`: `GET` paged (default 10, clamped, `PagingQueryValidator`), `POST` 201, `PUT /{noteId:guid}` 200, `DELETE /{noteId:guid}` 204; `WithName("ListJournalNotes")`, `"CreateJournalNote"`, `"UpdateJournalNote"`, `"DeleteJournalNote"`; 400/401/403/404 declared; never log body or category); register in `Program.cs`
- [X] T072 [P] [US5] Extend `src/MyGarage.Web/Vehicles/VehiclesApiClient.cs` with notes methods and create `src/MyGarage.Web/Vehicles/VehicleJournalSection.razor` (`add-note`, `edit-note-{noteId}`, `delete-note-{noteId}` forms; `notesPage` query parameter and `Pager`; category input with `maxlength="40"` and a label; encoded output only); include it in `VehicleDetail.razor`

**Checkpoint**: US5 tests pass; quickstart step 2.7 works.

---

## Phase 8: User Story 6 - Add a small gallery of images to a vehicle (Priority: P3)

**Goal**: Upload up to 10 JPEG/PNG/WebP images (max 5 MB each) per vehicle to private Azure Blob Storage, view them in a gallery, choose the main (card) picture, and delete images, with row and blob kept consistent.

**Independent Test**: Upload a valid image (appears, becomes the main/card picture), upload a second and set it main, reject a disguised text file and an oversize file, delete the main image (another is promoted), delete the vehicle (blobs removed); another user gets 404 for everything.

### Tests for User Story 6 (write first, confirm they fail)

- [X] T073 [P] [US6] Add `tests/MyGarage.Database.Tests/Common/AzuriteFixture.cs` (Testcontainers `AzuriteContainer`, exposes a `BlobServiceClient`, creates a uniquely named container per test class) and add it to `tests/MyGarage.Database.Tests/Common/DatabaseCollection.cs`
- [X] T074 [P] [US6] `VehicleImageRepositoryTests` in `tests/MyGarage.Database.Tests/VehicleImages/VehicleImageRepositoryTests.cs` (real PostgreSQL and Azurite): first image becomes main; blob `{vehicleId}/{imageId}` exists with the stored content type; 11th image -> limit outcome and no blob written; set-main clears the previous main in one transaction (unique partial index never violated); deleting the main promotes the oldest remaining; deleting the last image leaves no main; delete removes row then blob; deleting a vehicle (through `VehicleRepository`) removes all blobs under `{vehicleId}/`; a blob-store test double that throws on upload rolls back the row (nothing persisted); a failing blob delete after commit does not fail the operation and is logged with ids only; another owner's vehicle/image -> not found and no blob read, written, or deleted; metadata list never reads blob content
- [X] T075 [P] [US6] `ImageSignatureTests` in `tests/MyGarage.Api.Tests/Common/ImageSignatureTests.cs`: recognizes JPEG (`FF D8 FF`), PNG (`89 50 4E 47 0D 0A 1A 0A`), WebP (`RIFF` + 4 bytes + `WEBP`); rejects text bytes with `.jpg` name, GIF, empty, truncated headers; result is the detected content type regardless of the claimed type
- [X] T076 [P] [US6] `UploadImageRequestValidatorTests` in `tests/MyGarage.Api.Tests/Vehicles/Images/UploadImageRequestValidatorTests.cs`: file required and non-empty; length <= 5,242,880 bytes (boundary 5242880 ok, 5242881 rejected); caption optional <= 200 characters, no control characters
- [X] T077 [P] [US6] `VehicleImagesEndpointsTests` in `tests/MyGarage.Api.Tests/Vehicles/Images/VehicleImagesEndpointsTests.cs` (real Azurite via the API factory): `POST /api/vehicles/{id}/images` 201 `VehicleImageDto` with detected content type; 400 on a text file renamed `.jpg` or with a spoofed `image/jpeg` header; 413 or 400 over 5 MB; 409 at 10 images; 404 other user's vehicle; 401 without token; `GET .../content` returns the bytes with the stored content type, `X-Content-Type-Options: nosniff`, `Cache-Control: private, max-age=86400`; 404 for other user's image; `PUT .../main` 204 and 404; `DELETE` 204 and the blob is gone; deleting the vehicle removes its blobs; storage unavailable -> 503 `ProblemDetails` on upload and content while `GET /api/vehicles/{id}` still works; `GET /api/vehicles/{id}` and the list reflect `Images` and `MainImageId`
- [X] T078 [P] [US6] `VehicleImageEndpointsTests` in `tests/MyGarage.Web.Tests/Vehicles/VehicleImageEndpointsTests.cs` (WebApplicationFactory with fake API handler): `GET /vehicles/{id}/images/{imageId}` requires sign-in, forwards the bearer token, streams bytes with the API's content type, adds `nosniff` and private caching, passes through 404 and 503; `POST /vehicles/{id}/images` requires antiforgery and sign-in, enforces the size limit, forwards the stream to the API, redirects back to the vehicle with a success or failure message
- [X] T079 [P] [US6] `VehicleGallerySectionTests` in `tests/MyGarage.Web.Tests/Vehicles/VehicleGallerySectionTests.cs`: gallery thumbnails with `alt` = caption or nickname and `loading="lazy"`; main image marked; upload form (labeled file input with `accept="image/jpeg,image/png,image/webp"`, optional caption); set-main and delete buttons with accessible names; rejection messages for type, size, and limit; placeholder when there are no images

### Implementation for User Story 6

- [X] T080 [US6] Update `src/MyGarage.AppHost/AppHost.cs` and `AppHost.csproj` references: `builder.AddAzureStorage("storage").RunAsEmulator(emulator => emulator.WithDataVolume())` then `.AddBlobs("blobs")`; `api` gets `.WithReference(blobs).WaitFor(blobs)`; update `tests/MyGarage.AppHost.Tests/AppHostSmokeTests.cs` to also wait for `storage` and `blobs`; add `PackageReference` Aspire.Azure.Storage.Blobs to `src/MyGarage.Api/MyGarage.Api.csproj`
- [X] T081 [P] [US6] Create `src/MyGarage.Database/VehicleImages/` types: `VehicleImageRecord` (metadata only), `VehicleImageContent` (content type + readable `Stream`), `IVehicleImageBlobStore` (`UploadAsync(vehicleId, imageId, stream, contentType, ct)`, `OpenReadAsync`, `DeleteAsync`, `DeleteVehicleAsync(vehicleId)`, `EnsureContainerAsync`), and `AzureBlobVehicleImageStore` (container `vehicle-images`, blob name `{vehicleId}/{imageId}`, content type set on the blob, private access, no SAS or URL ever exposed)
- [X] T082 [P] [US6] Create scripts `src/MyGarage.Database/Scripts/VehicleImages/LockVehicle.sql`, `CountForVehicle.sql`, `Insert.sql`, `ListByVehicle.sql`, `GetById.sql` (owner-scoped through the parent vehicle, never selects blob data), `ClearMain.sql`, `SetMain.sql`, `Delete.sql`, `PromoteOldest.sql`; every statement filters through `EXISTS (SELECT 1 FROM vehicles WHERE id = @VehicleId AND owner_id = @OwnerId)` or the equivalent join
- [X] T083 [US6] Implement `src/MyGarage.Database/VehicleImages/VehicleImageRepository.cs` per research R9: add = one transaction (lock vehicle -> not found; count -> limit at 10; insert row with `is_main = true` when it is the first; upload blob; commit; on commit failure delete the blob best effort); set-main (clear then set in one transaction); delete (delete row and promote oldest in a transaction, commit, then delete blob; log failures with ids only); `OpenContentAsync` (owner-scoped metadata lookup, then blob stream; blob missing -> not found; storage failure surfaces a dedicated exception mapped to 503); register the repository and `AzureBlobVehicleImageStore` in `AddGarageDatabase` so T074 passes
- [X] T084 [US6] Extend `VehicleRepository.DeleteAsync` (or `VehicleService.DeleteAsync` coordinating `VehicleImageRepository`) so deleting a vehicle commits the row delete first and then removes all blobs under `{vehicleId}/` (best effort, ids only in logs), so the vehicle-deletion case in T074 and T077 passes
- [X] T085 [P] [US6] Implement `src/MyGarage.Api/Common/ImageSignature.cs` (detect JPEG/PNG/WebP from the first bytes of a seekable stream; return the content type or `null`) so T075 passes
- [X] T086 [P] [US6] Implement `src/MyGarage.Api/Vehicles/Images/UploadImageRequest.cs` (`[FromForm]` `IFormFile File`, `string? Caption`), `UploadImageRequestValidator.cs` (rules from T076; signature failure reported as a validation problem on `file`), `VehicleImageMapper.cs`, and `VehicleImagesEndpoints.cs` (`MapGroup("/api/vehicles/{vehicleId:guid}/images")`: `POST` multipart with `.DisableAntiforgery()` and a request size limit of about 5.5 MB -> 201/400/401/403/404/409/413/503; `GET /{imageId:guid}/content` streaming with the stored content type, `X-Content-Type-Options: nosniff`, `Cache-Control: private, max-age=86400`; `PUT /{imageId:guid}/main` 204; `DELETE /{imageId:guid}` 204; `WithName` values per `api.openapi.yaml`); map the storage exception to 503 `ProblemDetails`; register in `Program.cs`
- [X] T087 [US6] In `src/MyGarage.Api/Program.cs` add `builder.AddAzureBlobServiceClient("blobs")` and a hosted service in `src/MyGarage.Api/Vehicles/Images/ImageContainerHostedService.cs` that calls `EnsureContainerAsync` at startup (logs a warning, not a crash, if storage is unavailable); update `tests/MyGarage.Api.Tests/Common/ApiFactory.cs` to start Azurite (collection fixture) and set the `blobs` connection configuration; update `VehicleService`/`VehicleMapper` so `VehicleDetailDto.Images` and `VehicleSummaryDto.MainImageId` come from the image repository and `ListPage.sql` join
- [X] T088 [P] [US6] Create `src/MyGarage.Web/Vehicles/VehicleImageEndpoints.cs` (`MapVehicleImageEndpoints()`: `GET /vehicles/{vehicleId:guid}/images/{imageId:guid}` requiring sign-in and streaming from the API via `VehiclesApiClient` with the response headers from T086; `POST /vehicles/{vehicleId:guid}/images` with `IFormFile`, `.RequireAntiforgery()`, request size limit about 5.5 MB, forwarding the stream, redirecting to `/vehicles/{vehicleId}?imageStatus=...`); extend `VehiclesApiClient` with upload, set-main, delete, and content methods; register in `src/MyGarage.Web/Program.cs`
- [X] T089 [US6] Create `src/MyGarage.Web/Vehicles/VehicleGallerySection.razor` (gallery grid, main badge, upload `<form method="post" enctype="multipart/form-data">` with `<AntiforgeryToken />`, `set-main-{imageId}` and `delete-image-{imageId}` forms, status messages from `imageStatus`); include it in `VehicleDetail.razor`; ensure `VehicleCard.razor` uses `MainImageId` to render `/vehicles/{vehicleId}/images/{mainImageId}` so garage cards show the picture

**Checkpoint**: US6 tests pass; quickstart step 2.8 works including blob inspection.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T090 [P] Add `tests/MyGarage.Api.Tests/Vehicles/LoggingTests.cs`: run create, update, search, tag, note, and image flows with values such as nickname, search text, tag text, note body/category, and file bytes, and assert none of them appear in captured log output (FR-018)
- [X] T091 [P] Add `tests/MyGarage.Api.Tests/Vehicles/OwnershipIsolationTests.cs`: one table-driven test that, for every endpoint in `api.openapi.yaml`, asserts user B receives 404 (or an empty result for list/filter-options) for user A's vehicle, tag, note, and image ids and that no data was changed (SC-003, SC-010)
- [X] T092 [P] Propose constitution v1.1.3 in `.specify/memory/constitution.md` (PATCH): clarify that `MyGarage.Database` also holds blob-backed persistence stores (the image store), and add the `Azure.Storage.Blobs` dependency note; submit via the pull request for reviewer approval per Governance
- [X] T093 [P] Update `infra/README.md` with storage requirements for Azure deployment: private container `vehicle-images`, anonymous access disabled, shared-key access disabled, managed-identity role assignment *Storage Blob Data Contributor* for the API, and the configuration keys the Aspire Blob client reads; update `src/README.md` and `tests/README.md` with the new `MyGarage.Database` and `MyGarage.Database.Tests` projects and the Docker requirement
- [X] T094 [P] Accessibility and presentation pass on `src/MyGarage.Web/Vehicles/*.razor`: keyboard order, visible focus, labels and `aria-describedby` for validation messages, accessible names on icon-only controls, `prefers-reduced-motion` respected, narrow-screen layout (FR-019); confirm no `MarkupString` and no jQuery/custom JS was added
- [X] T095 Run the full quickstart in `specs/002-vehicle-tracking/quickstart.md` (manual sections 2-4 and `dotnet test MyGarage.slnx` with Docker running); confirm the solution builds with zero warnings, all tests pass, migrations apply to an empty database, and fix any gaps found

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies; start immediately
- **Foundational (Phase 2)**: depends on Setup; BLOCKS all user stories
- **US1 (Phase 3)** depends on Foundational only; it is the MVP
- **US2 (Phase 4)** depends on US1 (extends the list); tag search and tag filter behave fully once US4 adds tags (its tests seed tags directly through SQL)
- **US3 (Phase 5)** depends on US1 (extends detail and service)
- **US4 (Phase 6)**, **US5 (Phase 7)** depend on US1 (detail page and service) and are independent of each other
- **US6 (Phase 8)** depends on US1; it extends the delete flow from US3 (T084) so do it after US3
- **Polish (Phase 9)** depends on all stories being complete

### Within Each User Story

- Tests first and failing, then records/scripts, then repository, then Api validators/mappers/endpoints, then Web client and pages
- A task that edits a file another task also edits (`VehicleService.cs`, `VehiclesEndpoints.cs`, `VehiclesApiClient.cs`, `VehicleDetail.razor`, `Program.cs`, `AddGarageDatabase`) is not parallel with it

### Story completion order

US1 -> (US2, US3, US4, US5 in any order or parallel with separate owners, resolving the shared-file edits above) -> US6 -> Polish

### Parallel Opportunities

- Setup: T003 alongside T002
- Foundational: T005-T008 together; T009-T014 together after the tests exist; T017-T019 together
- Each story: all test tasks marked [P] together; script/record tasks and Web section components in different files together
- After US1, US4 and US5 can be developed by different people since each owns its own folders (only the small shared-file edits need merging)

### Parallel Example: User Story 1

```text
Together: T024, T025, T026, T027, T028, T029 (all test files)
Together: T030, T032, T034, T035, T036 (different files)
Then: T031 -> T033 -> T037 -> T038 -> T039
```

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 and Phase 2
2. Complete Phase 3 (US1) and validate with quickstart steps 2.1-2.3 and the isolation check
3. Demo: users can sign in, add vehicles, and browse them as paged cards with only their own data

### Incremental Delivery

1. Add US2 (search and filter) and US3 (edit and remove): the garage becomes usable day to day
2. Add US4 (tags) and US5 (journal): richer vehicle history
3. Add US6 (image gallery on Blob Storage): pictures on cards and in a gallery
4. Finish with Polish (logging checks, isolation sweep, constitution PATCH, infra docs, accessibility, full quickstart)

## Notes

- `[P]` tasks touch different files with no incomplete dependencies
- Each story's checkpoint is an independently demonstrable increment
- Quote and keep the validation limits exactly as written; if a limit must change, update `data-model.md` and `research.md` R13 first
- Commit after each task or logical group on branch `002-vehicle-tracking`; Conventional Commits
