# Implementation Plan: Archive Vehicle

**Branch**: `014-archive-vehicle` | **Date**: 2026-11-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/014-archive-vehicle/spec.md`

## Summary

A signed-in user can **archive** a vehicle they no longer own instead of deleting it. An archived vehicle keeps all of its history, leaves the default vehicle list, becomes read-only, and drops out of the garage figures, with one exception: its fuel and charging entries stay in the fuel and charging figures (entry count and the monthly spend chart). The vehicle list and search gain a **status filter** (Active by default, Archived, All) instead of a separate archive page. Archiving **disables every reminder** on the vehicle without deleting any, so tax-due and registration-due reminders stop counting, stop appearing in "needs attention", and stop raising notices. **Restore** returns everything exactly as it was. **Delete** is unchanged and works for either state.

The design leans on what already exists. Archived state is one nullable column, `vehicles.archived_at` (migration `0018`). A reminder is disabled by derivation: `ReminderStatusCalculator` returns a new `Archived` status when its vehicle is archived, the two SQL scripts that feed every reminder count and list (`ListForOwner`, `ListForEvaluation`) skip archived vehicles, and nothing on the reminder rows changes, so restore is lossless and a reminder the user paused themselves stays paused. Read-only is enforced in one place, an endpoint filter added to every vehicle-scoped group and to the vehicle update, with a test that walks the endpoint table so a future feature cannot skip it. The dashboard, drafts, and vehicle list each change by a one-line predicate per script.

No new package, project, container, or infrastructure. One migration, two new endpoints, one new filter, one new reminder status, one new query parameter.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (pinned by `global.json`)

**Primary Dependencies**: Existing only: ASP.NET Core minimal APIs, FluentValidation, Npgsql, Dapper, Serilog, OpenTelemetry, Blazor static SSR, Bootstrap 5.3.x. **New**: none.

**Storage**: PostgreSQL via forward-only migration `0018_add_vehicle_archive.sql`: `vehicles.archived_at timestamptz NULL` and a partial index on active vehicles. No blob storage change.

**Testing**: xUnit; Testcontainers PostgreSQL for `MyGarage.Database.Tests` and `MyGarage.Api.Tests` (no mocked database); bUnit and `WebApplicationFactory` with AngleSharp for `MyGarage.Web.Tests`; `UiStandardConformanceTests` covers the changed `.razor` files. Pure unit tests for the status calculator. A route-walking conformance test over the API endpoint table. Docker required for database and API tests.

**Target Platform**: Local development with Aspire; Azure for the deployed app. Nothing in `infra/` changes.

**Project Type**: Web application (Blazor static SSR + minimal API + PostgreSQL), Aspire-orchestrated.

**Performance Goals**: Vehicle list, dashboard, and reminder lists answer in under 1 s for 95% of calls at the expected size (tens of vehicles per user), unchanged by this feature. The active-vehicle partial index keeps the default list as fast as today. The write guard adds one indexed single-row read per write. Spec SC-001 and SC-004 are form-flow targets, not server limits.

**Constraints**: Owner derived only from validated claims; every statement scoped to the owner in SQL; another user's vehicle answers 404 on archive, restore, and every other operation, including through the guard. Parameterized SQL in `.sql` files only; async with `CancellationToken`. No vehicle name, VIN, or record data in logs. Archived-state refusals are `409` problem details of one documented type.

**Scale/Scope**: Tens of users, a handful of vehicles each. Two new endpoints, one endpoint filter applied to 11 route groups and the vehicle update, one migration, about 5 new and 14 changed SQL scripts, one new reminder status, changes to the vehicle list, vehicle detail, dashboard chart legend, reminder rows, and draft review in the Web.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. .NET 10 / C# only; nullable on; warnings as errors; async + `CancellationToken` | PASS | All new code async and forwards tokens. |
| II. Feature-folder organization | PASS | Archive code lives in the existing `Vehicles/` folders (Shared, Database, Api, Web) and their test folders; reminder changes stay in `Reminders/`, dashboard changes in `Dashboard/`, draft changes in `Drafts/`, notification SQL in `Notifications/`. The shared guard is cross-feature code and lives in `Api/Common` next to `ValidationFilter`. |
| III. Solution structure & dependency direction | PASS | No new project or reference. Web reaches data only through `VehiclesApiClient`; Database references no other MyGarage project (the archived flag crosses as a plain value and is mapped in the Api). |
| IV. DTO boundary | PASS | `ArchivedAt` is added to `VehicleSummaryDto` and `VehicleDetailDto`, `Archived` to `ReminderStatus`, `VehicleArchived` to `FuelEnergySpendVehicleDto` and `ReminderDto`, all in `MyGarage.Shared`; records stay internal to Database and the Api mapper; explicit mappers are tested. |
| V. Validated minimal APIs | PASS | The new `status` query value is validated by `VehicleListQueryValidator` before data access; archive and restore take no body and are checked by the guard-free state change itself; `ProblemDetails` for every refusal; OpenAPI metadata declared (see `contracts/`). |
| VI. Dapper, PostgreSQL, SQL scripts | PASS | New and changed scripts under `Scripts/Vehicles`, `Scripts/Reminders`, `Scripts/Dashboard`, `Scripts/Drafts`, and `Scripts/Notifications`; no SQL in C#; ownership proved in SQL. |
| VII. Versioned migrations | PASS | `0018_add_vehicle_archive.sql`, forward-only; `0001` to `0017` untouched. Migration-count tests are bumped. |
| VIII. Test-first, real dependencies | PASS | Failing tests first for the migration, repository, calculator, status filter validator, archive and restore endpoints (success, not found, other owner, already in state), the guard on every route group, the route-walking test, dashboard figures, reminder lists and evaluator, drafts, and Web pages. Real PostgreSQL; nothing mocked. |
| Auth: Entra OIDC + JWT; every endpoint authorized | PASS | Archive and restore are user endpoints under the fallback policy and refuse app-only tokens. None is anonymous or trusted-service. |
| Observability | PASS | Logs carry ids and outcomes only. New counters: `vehicles.archived`, `vehicles.restored`. |
| Data protection: ownership from claims; SQL scoped by owner | PASS | The archived state belongs to the owner's vehicle; every new statement is scoped by `owner_id`; nothing in a request names an owner. Archive and restore are attributed through `updated_by`. |
| Dependencies justified | PASS | No new packages. |

**Post-design re-check (after Phase 1)**: unchanged. No Complexity Tracking entries.

## Project Structure

### Documentation (this feature)

```text
specs/014-archive-vehicle/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── api.openapi.yaml   # Archive, restore, list status filter, 409 write refusal, changed schemas
│   └── web-routes.md      # List filter, vehicle page states, reminder row, dashboard legend, drafts
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── MyGarage.Shared/
│   ├── Vehicles/         # VehicleSummaryDto + ArchivedAt, VehicleDetailDto + ArchivedAt, VehicleStatusFilter values
│   ├── Reminders/        # ReminderStatus + Archived, ReminderDto + VehicleArchived
│   └── Dashboard/        # FuelEnergySpendVehicleDto + VehicleArchived
├── MyGarage.Database/
│   ├── Vehicles/         # VehicleRepository + ArchiveAsync, RestoreAsync, GetArchivedStateAsync, status criteria; records + ArchivedAt
│   ├── Reminders/        # ReminderRecord / ReminderForEvaluationRecord + VehicleArchived
│   ├── Dashboard/        # FuelEnergySpendRecord + VehicleArchived
│   ├── Drafts/           # draft read records unchanged in shape (vehicle becomes null for an archived vehicle)
│   └── Scripts/
│       ├── Migrations/0018_add_vehicle_archive.sql
│       ├── Vehicles/     # NEW Archive, Restore, GetArchivedState; CHANGED ListPage, Count, FilterOptions, GetById
│       ├── Reminders/    # CHANGED ListForVehicle, GetById, ListForOwner, ListForEvaluation
│       ├── Notifications/# NEW MarkReadForVehicleReminders
│       ├── Dashboard/    # CHANGED Counts, RecentActivity, FuelEnergySpend
│       └── Drafts/       # CHANGED ListOwnerVehicles, ListOpenPage, GetById
├── MyGarage.Api/
│   ├── Common/           # RequireActiveVehicleFilter (+ RequireActiveVehicleForWrites extension), problem type constant
│   ├── Vehicles/         # VehiclesEndpoints: archive, restore, guard on PUT; VehicleListQuery + Status; VehicleService; VehicleMapper
│   ├── Reminders/        # ReminderStatusCalculator (+ VehicleArchived), ReminderMapper, ReminderOrdering, RemindersEndpoints guard
│   ├── Dashboard/        # DashboardService/mapper: archived flag on the spend vehicles
│   ├── Drafts/           # DraftService: archived vehicle refused on include
│   └── (FuelEnergyEntries, Inspections, Insurance, Registrations, Taxes, ServiceRecords, VehicleDocuments, Vehicles/Images,
│        Vehicles/JournalNotes, Vehicles/Tags)  # one-line guard on each group
└── MyGarage.Web/
    ├── Vehicles/         # VehicleListCriteria + Status, VehicleList + VehicleSearchPage status links, VehicleCard badge,
    │                     # VehicleDetail (archive, restore, read-only), VehicleArchivedState, ArchivedVehicleNotice, ApiOutcome.Archived
    ├── Reminders/        # reminder rows and detail: Archived badge and wording; hide actions
    ├── Dashboard/        # spend legend and tooltips: "(archived)"
    ├── Drafts/           # nothing changes in markup; archived vehicles no longer arrive from the API
    ├── Maintenance/      # sections honor VehicleArchivedState (no Add, Import, edit, or delete controls)
    └── Help/             # FAQ: archiving, restoring, and the Archived filter

tests/
├── MyGarage.Database.Tests/   # migration 0018 (count bumps), VehicleRepository archive/restore/list status, reminders/dashboard/drafts SQL
├── MyGarage.Api.Tests/        # calculator, validator, archive/restore endpoints, guard on every group, route-walking test,
│                              # dashboard, reminders list and evaluator, notifications, drafts
└── MyGarage.Web.Tests/        # list filter, vehicle page both states, read-only sections, reminder rows, dashboard legend, UI conformance
```

**Structure Decision**: No new feature folder: archiving is a state of the vehicle, so its code sits with `Vehicles` in every project, and the consequences land in the features that already own those behaviors (`Reminders`, `Dashboard`, `Drafts`, `Notifications`). The write guard is one reusable endpoint filter next to the existing `ValidationFilter`, applied per group, so the rule is defined once and a route-walking test protects it.

## Complexity Tracking

No constitution violations to justify.
