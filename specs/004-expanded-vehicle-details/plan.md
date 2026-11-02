# Implementation Plan: Expanded Vehicle Details and Lookup Preferences

**Branch**: `004-expanded-vehicle-details` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/004-expanded-vehicle-details/spec.md`

## Summary

Extend the vehicle with the fields in the spec (VIN, year, vehicle type, purchased/leased, purchase and lease-end dates, engine, horsepower, fuel type, transmission, seats, drivetrain, wheel size, tire size), regroup the vehicle form and detail page into the Basic Info and Specifications tabs, and add user-managed lookups. Vehicle Types, Fuel Types, Transmissions, and Drivetrains ship with application defaults available to every user; each user can hide/show any item and add their own items from a new **Preferences** page (reached from the account menu), which also shows read-only account details and one tab per lookup. Garage cards show the year; search and filters cover the new fields.

Technical approach: two forward-only migrations (`lookup_items` + per-user `lookup_item_hidden`, defaults seeded with fixed ids; new nullable `vehicles` columns with check constraints and foreign keys to lookup items). A new `Lookups` feature in Shared (DTOs), Database (repository + SQL), Api (`/api/lookups` group, validators, service), and Web (typed client). The existing `Vehicles` feature gains the new fields in its DTOs, validator, mapper, SQL, forms, detail page, cards, and garage filters. A new `Preferences` page in Web (static SSR, post-redirect-get forms like the vehicle pages) uses a shared Bootstrap tab component extracted from the vehicle tab navigation. No new packages, projects, or infrastructure.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (pinned by `global.json`)

**Primary Dependencies**: Existing only: Aspire 13.x, `Microsoft.Identity.Web`, FluentValidation, ASP.NET Core OpenAPI, Npgsql, Dapper, Serilog, OpenTelemetry, Bootstrap 5.3.x. No new packages (see [research.md](research.md) R15).

**Storage**: PostgreSQL. New tables `lookup_items` and `lookup_item_hidden`; new columns on `vehicles`; migrations `0005` and `0006` (applied by the existing migration runner with checksum ledger). No blob storage changes.

**Testing**: xUnit; Testcontainers PostgreSQL for `MyGarage.Database.Tests` and `MyGarage.Api.Tests` (no mocked database); bUnit and `WebApplicationFactory` for `MyGarage.Web.Tests`; existing `UiStandardConformanceTests` extended for the new page and menu entry. Docker required for database and API tests.

**Target Platform**: Local development with Aspire on Windows/macOS/Linux with Docker or Podman; unchanged Entra setup.

**Project Type**: Web application (Blazor Web App UI + minimal API + PostgreSQL), Aspire-orchestrated.

**Performance Goals**: Garage page, search, and filters < 2 s for 100 vehicles (SC-006); a lookup change visible on the next vehicle form (SC-003); Preferences page (four small lists) renders in a single API call.

**Constraints**: Owner derived only from validated token claims; every owner-specific SQL statement filters on `owner_id` (custom lookup items and visibility rows included; defaults are the only rows with a null owner and are read-only); no entity types across the API boundary; parameterized SQL in `.sql` files only; no VIN, vehicle values, or custom item names in logs or URLs; lookup item names 1-30 characters, at most 50 custom items per user per lookup; free of control characters.

**Scale/Scope**: Tens of vehicles per user; 4 lookups with roughly 5-12 defaults each; 3 new API endpoints (`GET /api/lookups`, `POST /api/lookups/{kind}`, `PUT /api/lookups/{kind}/{itemId}/visibility`) plus changes to 4 existing vehicle endpoints; 1 new Web page (`/preferences`); 2 migrations.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. .NET 10 / C# only; nullable on; warnings as errors; async + `CancellationToken` | PASS | All new repository, service, endpoint, and client methods are async and take a `CancellationToken`. New optional fields are nullable reference/value types. |
| II. Feature-folder organization | PASS | New `Lookups/` feature folders in Shared, Database (+ `Scripts/Lookups/`), Api, and Web (client); new `Preferences/` folder in Web for the page; vehicle changes stay in `Vehicles/`. The shared tab component goes in `Common/`. Test projects mirror (`Lookups/`, `Preferences/`). |
| III. Solution structure & dependency direction | PASS | No new projects or references. Web talks to Api over HTTP only; Api uses repositories; Database references nothing. |
| IV. DTO boundary | PASS | New DTOs (`LookupsDto`, `LookupItemDto`, `LookupValueDto`, request types) in `MyGarage.Shared`; database records stay internal; explicit mappers with tests. The ownership choice is a Shared enum mapped explicitly to a stored text value. |
| V. Validated minimal APIs | PASS | New group `/api/lookups` via `MapGroup`; every request DTO has a FluentValidation validator; kind route values are validated; `ProblemDetails` for 400/401/403/404/409; OpenAPI metadata declared. |
| VI. Dapper, PostgreSQL, SQL scripts | PASS | All SQL in `Scripts/Lookups/` and updated `Scripts/Vehicles/` files, parameterized, loaded by the existing script store; repositories in Database. |
| VII. Versioned migrations | PASS | `0005_create_lookup_items.sql` and `0006_add_vehicle_details.sql`, forward-only; applied scripts 0001-0004 are not edited. |
| VIII. Test-first, real dependencies | PASS | Failing tests first for migrations (defaults seeded, applies to empty DB), lookup and vehicle repositories (owner isolation, uniqueness, limits, filters), endpoints (success, validation, not-found, 401, cross-user), validators, mappers, and Web components/pages. |
| Auth: Entra OIDC + JWT; every endpoint authorized | PASS | New endpoints and `/preferences` inherit the fallback policy; none is anonymous. |
| Observability: Serilog + OpenTelemetry | PASS | Logs carry ids, counts, and outcomes only; no VIN, vehicle field values, or custom item names. |
| Data protection: ownership from claims; SQL scoped by owner | PASS | Custom items and visibility rows carry `owner_id`; visibility and add statements verify the item is a default or owned by the caller; another user's item id returns 404. Vehicle lookup references are accepted only when the item is a default or owned by the caller. |
| Dependencies justified | PASS | None added. |

**Post-design re-check (after Phase 1)**: unchanged; no violations and nothing recorded under Complexity Tracking beyond the notes below.

## Project Structure

### Documentation (this feature)

```text
specs/004-expanded-vehicle-details/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── api.openapi.yaml      # changes to the vehicles API and the new lookups API
│   └── web-routes.md         # /preferences, vehicle tabs, nav menu entry
├── checklists/
│   └── requirements.md
└── tasks.md                  # created by /speckit-tasks
```

### Source Code (repository root)

```text
docs/ui-standard.md                     # account menu gains Preferences; vehicle tabs list the new fields; Preferences page pattern

src/
├── MyGarage.Database/
│   ├── Lookups/                        # NEW: LookupRepository, LookupItemRecord, LookupKind (SQL names)
│   ├── Vehicles/                       # VehicleRecord/SummaryRecord/FilterOptionsRecord/SearchCriteria gain the new fields
│   └── Scripts/
│       ├── Migrations/                 # + 0005_create_lookup_items.sql, 0006_add_vehicle_details.sql
│       ├── Lookups/                    # List.sql, GetVisibleItem.sql, Insert.sql, CountCustom.sql, LockOwnerKind.sql, Hide.sql, Show.sql
│       └── Vehicles/                   # Insert/Update/GetById/ListPage/Count/FilterOptions updated for new columns and filters
├── MyGarage.Api/
│   ├── Program.cs                      # + AddLookups(), MapLookupsEndpoints()
│   ├── Lookups/                        # NEW: LookupsEndpoints, LookupService, request validators, LookupMapper
│   └── Vehicles/                       # VehicleRequestValidator, VehicleMapper, VehicleService (lookup availability check), VehicleListQuery
├── MyGarage.Shared/
│   ├── Lookups/                        # NEW: LookupKinds, LookupsDto, LookupItemDto, LookupValueDto, AddLookupItemRequest, SetLookupVisibilityRequest
│   └── Vehicles/                       # VehicleRequest/VehicleDetailDto/VehicleSummaryDto/VehicleFilterOptionsDto gain the new fields; OwnershipType enum
└── MyGarage.Web/
    ├── Common/                         # + TabNav.razor (generic tab strip extracted from VehicleTabNav)
    ├── Components/Layout/AppNavbar.razor # account menu: + Preferences
    ├── Lookups/                        # NEW: LookupsApiClient
    ├── Preferences/                    # NEW: Preferences.razor (account details + tabs), AccountDetails.razor, LookupTab.razor, PreferencesTabs.cs
    └── Vehicles/                       # VehicleBasicFields/VehicleSpecificationFields/VehicleFormModel/VehicleFormFields/VehicleTabs/VehicleDetail/VehicleCard/VehicleSearchCard/VehicleListCriteria updated

tests/
├── MyGarage.Database.Tests/            # + Lookups/, Common/ (migration tests for 0005/0006), Vehicles/ (new columns, filters, search)
├── MyGarage.Api.Tests/                 # + Lookups/, Vehicles/ (validators, endpoints, mappers, cross-user)
└── MyGarage.Web.Tests/                 # + Preferences/, Vehicles/ (forms/tabs/cards/filters), Components/ (navbar menu, UI conformance)
```

**Structure Decision**: Extend the existing multi-project solution without new projects. Lookups are a feature of their own (their own tables, repository, endpoints, and DTOs) because they will be reused by future features such as service records; the Preferences page is a Web-only presentation feature that consumes lookups and the existing current-user endpoint.

## Complexity Tracking

No constitution violations. Two notes for review:

| Note | Reason |
|---|---|
| Default lookup items are data seeded by a migration (fixed ids) | Constitution VII requires all schema/data changes to be forward-only migrations; seeding in a script keeps defaults versioned, testable on an empty database, and addable later (R2). |
| Lookup kind and availability for a vehicle reference are checked in the Api service and SQL, not by a database composite key | A single `lookup_items` table keeps four near-identical lists in one repository; the foreign key guarantees the item exists, and the service guarantees it is the right kind and available to the owner (R3). |
