# Implementation Plan: Vehicle Service Records

**Branch**: `005-service-records` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/005-service-records/spec.md`

## Summary

Each vehicle gets its own collection of service records. A record holds **Date Serviced**, **Odometer Reading**, and **Shop Name or Person** (open text). The user then adds **line items** that say what was done and what it cost, either as a flat cost or as a breakdown of labor (hours at an hourly rate) and parts (name, quantity, price per part). The record's total cost is the sum of its line items. Records appear on a new **Service Records** tab of the vehicle detail page, most recent Date Serviced first, and can be edited and deleted. A new vehicle field, **Latest Odometer Reading**, is added, and the mileage on the My Vehicles card comes from the greater of the latest service record's odometer, that field, and the existing purchase mileage.

Technical approach: two forward-only migrations (three service tables with cascading foreign keys; one new nullable vehicle column). A new `ServiceRecords` feature in Shared (DTOs), Database (repository, cost calculator, SQL), Api (`/api/vehicles/{vehicleId}/service-records` group, validators, mapper), and Web (typed client, tab, and pages). The `Vehicles` feature gains the new field and the effective-mileage SQL. All ownership checks go through the parent vehicle in SQL. No new packages, projects, or infrastructure.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (pinned by `global.json`)

**Primary Dependencies**: Existing only: Aspire 13.x, `Microsoft.Identity.Web`, FluentValidation, ASP.NET Core OpenAPI, Npgsql, Dapper, Serilog, OpenTelemetry, Bootstrap 5.3.x. No new packages (see [research.md](research.md) R15).

**Storage**: PostgreSQL. New tables `service_records`, `service_line_items`, `service_line_item_parts` (migration `0009`) and a new `vehicles.latest_odometer_reading` column (migration `0010`), applied by the existing migration runner with checksum ledger. No blob storage changes.

**Testing**: xUnit; Testcontainers PostgreSQL for `MyGarage.Database.Tests` and `MyGarage.Api.Tests` (no mocked database); bUnit and `WebApplicationFactory` for `MyGarage.Web.Tests`; `UiStandardConformanceTests` extended for the new pages. Docker required for database and API tests.

**Target Platform**: Local development with Aspire on Windows/macOS/Linux with Docker or Podman; unchanged Entra setup.

**Project Type**: Web application (Blazor Web App UI + minimal API + PostgreSQL), Aspire-orchestrated.

**Performance Goals**: First page of a vehicle's records < 2 s with 500 records (SC-003), served by one composite index; the garage list stays < 2 s for 100 vehicles with the effective-mileage lookup (a lateral index probe per vehicle).

**Constraints**: Owner derived only from validated token claims; every service-record statement proves ownership through `vehicles.owner_id` and the route's vehicle and record ids; DTOs only across the API boundary; parameterized SQL in `.sql` files only; no shop names, descriptions, part names, or amounts in logs or URLs; text fields free of control characters; money values limited to 2 decimals and 0-9,999,999.99; at most 50 line items per record and 25 parts per line item.

**Scale/Scope**: Tens of vehicles per user; up to hundreds of records per vehicle; 8 new API endpoints (list/create record, get/update/delete record, add line item, update/delete line item) plus changes to the vehicle payloads; 5 new Web pages and one new tab; 2 migrations.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. .NET 10 / C# only; nullable on; warnings as errors; async + `CancellationToken` | PASS | All new repository, endpoint, and client methods are async and take a `CancellationToken`. Optional values are nullable. |
| II. Feature-folder organization | PASS | New `ServiceRecords/` folders in Shared, Database (+ `Scripts/ServiceRecords/`), Api, Web, and mirrored in each test project; the vehicle field and effective-mileage SQL stay in `Vehicles/`. |
| III. Solution structure & dependency direction | PASS | No new projects or references. Web talks to Api over HTTP only; Api uses repositories; Database references nothing. |
| IV. DTO boundary | PASS | New DTOs in `MyGarage.Shared`; database records stay internal; explicit `ServiceRecordMapper` with tests. |
| V. Validated minimal APIs | PASS | `MapGroup("/api/vehicles/{vehicleId:guid}/service-records")`; every request DTO has a FluentValidation validator wired through `ValidationFilter<T>`; `ProblemDetails` for 400/401/403/404/409; OpenAPI metadata declared. |
| VI. Dapper, PostgreSQL, SQL scripts | PASS | All SQL in `Scripts/ServiceRecords/`, parameterized, loaded by the existing script store; repository in Database. |
| VII. Versioned migrations | PASS | `0009_create_service_records.sql`, `0010_add_vehicle_latest_odometer.sql`, forward-only; 0001-0008 untouched. |
| VIII. Test-first, real dependencies | PASS | Failing tests first for migrations, repository, calculator, validators, mappers, endpoints (success, validation, not found, 401, cross-user, 409), and Web components/pages. |
| Auth: Entra OIDC + JWT; every endpoint authorized | PASS | All new endpoints and pages inherit the fallback policy; none is anonymous. |
| Observability: Serilog + OpenTelemetry | PASS | Logs carry ids, counts, and outcomes only; no service record contents. |
| Data protection: ownership from claims; SQL scoped by owner; service-record operations verified via parent vehicle | PASS | No `owner_id` on service tables; every statement joins to `vehicles` with `owner_id = @OwnerId` plus the vehicle id from the route, including access by record id and line item id (R2). `created_by`/`updated_by` record the authenticated owner. |
| Dependencies justified | PASS | None added. |

**Post-design re-check (after Phase 1)**: unchanged; no violations. Two review notes are under Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/005-service-records/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── api.openapi.yaml      # service-records API
│   └── web-routes.md         # Service Records tab, record and line item pages, vehicle changes
├── checklists/
│   └── requirements.md
└── tasks.md                  # created by /speckit-tasks
```

### Source Code (repository root)

```text
docs/ui-standard.md                     # vehicle tabs gain Service Records; record and line item page patterns

src/
├── MyGarage.Database/
│   ├── ServiceRecords/                 # NEW: ServiceRecordRepository, ServiceCostCalculator, ServiceRecordRecord, ServiceLineItemRecord, ServiceLinePartRecord, ServiceRecordDetailRecord
│   ├── Vehicles/                       # VehicleRecord gains LatestOdometerReading; summary Mileage is the effective mileage
│   └── Scripts/
│       ├── Migrations/                 # + 0009_create_service_records.sql, 0010_add_vehicle_latest_odometer.sql
│       ├── ServiceRecords/             # VehicleExists, ListPage, Count, Insert, GetById, Update, Delete, LockRecord, CountLineItems, InsertLineItem, UpdateLineItem, DeleteLineItem, ReplaceParts (delete + insert), ListLineItems, ListParts
│       └── Vehicles/                   # Insert/Update/GetById gain the new column; ListPage/Count use the effective-mileage lateral join
├── MyGarage.Api/
│   ├── Program.cs                      # + AddServiceRecords(), MapServiceRecordsEndpoints()
│   ├── ServiceRecords/                 # NEW: ServiceRecordsEndpoints, ServiceRecordRequestValidator, ServiceLineItemRequestValidator, ServicePartRequestValidator, ServiceRecordMapper
│   └── Vehicles/                       # VehicleRequestValidator (latest odometer rule), VehicleMapper
├── MyGarage.Shared/
│   ├── ServiceRecords/                 # NEW: the DTOs listed in data-model.md
│   └── Vehicles/                       # VehicleRequest/VehicleDetailDto gain LatestOdometerReading; VehicleSummaryDto.Mileage comment
└── MyGarage.Web/
    ├── Common/                         # Icon/IconName gain Wrench
    ├── ServiceRecords/                 # NEW: ServiceRecordsApiClient, form models, ServiceRecordFormat, ServiceRecordsView.razor (tab), ServiceRecordCreate/Detail/Edit.razor, ServiceLineItemCreate/Edit.razor, ServiceLineItemForm.razor
    └── Vehicles/                       # VehicleTabs (+ service-records), VehicleDetail (tab, latest odometer, delete text), VehicleBasicFields/VehicleFormModel/VehicleFormFields (latest odometer)

tests/
├── MyGarage.Database.Tests/            # + ServiceRecords/, Common/ (migration 0009/0010), Vehicles/ (effective mileage in list, count, filters)
├── MyGarage.Api.Tests/                 # + ServiceRecords/ (validators, mapper, endpoints, cross-user), Vehicles/ (latest odometer)
└── MyGarage.Web.Tests/                 # + ServiceRecords/ (tab, list, record page, forms), Vehicles/ (tab strip, latest odometer), Components/ (UI conformance)
```

**Structure Decision**: Extend the existing multi-project solution without new projects. Service records are a feature of their own (their own tables, repository, endpoints, DTOs, and pages) attached to a vehicle by route and foreign key, following the journal-notes pattern for ownership through the parent vehicle and the lookups feature for a self-contained top-level folder.

## Complexity Tracking

No constitution violations. Two notes for review:

| Note | Reason |
|---|---|
| `service_line_items.total_cost` is stored (derived) data | Computed in one place (`ServiceCostCalculator`) on every write so record totals and paged lists are a single `SUM` in SQL and cannot disagree with the detail view (R6). The repository's write methods are the only writers. |
| Garage mileage is derived from three sources (latest-dated record odometer, Latest Odometer Reading, purchase mileage) | The product owner asked for the card to follow "the latest service record or" the new field; taking the greatest non-null value needs no timestamp on the manual field and keeps existing vehicles unchanged (R8). Documented for the owner to confirm. |

## Open points for the product owner (defaults chosen; easy to change)

1. **Hourly rate**: labor needs a rate to produce a cost, so the labor line takes hours and an hourly rate (R5).
2. **Mileage rule**: the card shows the greater of the latest record's odometer, Latest Odometer Reading, and the existing purchase mileage (R8).
3. **Line item modes**: flat cost or breakdown, never both; a free service is a flat 0.00 (R4).
4. **Required fields**: Date Serviced and Odometer Reading are required; Shop Name or Person is optional (R9).
5. **Future dates**: a Date Serviced in the future is rejected (R9).
