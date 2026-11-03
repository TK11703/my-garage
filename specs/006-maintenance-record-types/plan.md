# Implementation Plan: Charging and Fueling, Inspections, Registrations, Insurance, and Taxes

**Branch**: `006-maintenance-record-types` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/006-maintenance-record-types/spec.md`

## Summary

The vehicle's **Maintenance** tab keeps its accordion but now has three sections: **Service Records** (unchanged), **Charging and Fueling**, and **Inspections, Registrations, Insurance, and Taxes**. Five new kinds of vehicle-owned entry are added:

- **Fuel or charging entry**: kind (fuel or energy), date, price per gallon or kWh, total gallons or kWh delivered, and total cost (calculated from price and amount when left empty).
- **Inspection**: type (Safety or Emissions), date, cost, optional next due date.
- **Registration**: state, cost, expiration date.
- **Insurance**: start date, end date, duration (how often the premium is paid: Monthly, Bi-Annual, or Annual), and cost (the premium paid each time); nothing else.
- **Tax**: type (Personal Property, Lease, Purchase), amount, recurrence (Once, Monthly, Bi-Annual, Annual).

Each kind is a simple flat record: no line items and no detail page. Lists live in the Maintenance tab (paged, newest first); each row has an Edit link, and the edit page holds the delete confirmation. After a save or delete the user returns to the Maintenance tab with the relevant accordion section open.

Technical approach: one forward-only migration (`0011`) creating five tables with cascading foreign keys to `vehicles`. Five new feature folders (`FuelEnergyEntries`, `Inspections`, `Registrations`, `Insurance`, `Taxes`) in Shared (DTOs), Database (repository and SQL), Api (endpoint group per feature, validators, mapper), and Web (typed client, form, create and edit pages, list section). The accordion shell moves to a new `Maintenance/` Web folder and the service-records body moves to its own section component. Every ownership check goes through the parent vehicle in SQL, exactly as in feature 005. No new packages, projects, or infrastructure.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (pinned by `global.json`)

**Primary Dependencies**: Existing only: Aspire 13.x, `Microsoft.Identity.Web`, FluentValidation, ASP.NET Core OpenAPI, Npgsql, Dapper, Serilog, OpenTelemetry, Bootstrap 5.3.x. No new packages (see [research.md](research.md) R16).

**Storage**: PostgreSQL. New tables `fuel_energy_entries`, `inspections`, `registrations`, `insurance_policies`, `taxes` in migration `0011_create_maintenance_records.sql`, applied by the existing migration runner with checksum ledger. No blob storage changes.

**Testing**: xUnit; Testcontainers PostgreSQL for `MyGarage.Database.Tests` and `MyGarage.Api.Tests` (no mocked database); bUnit and `WebApplicationFactory` for `MyGarage.Web.Tests`; `UiStandardConformanceTests` extended for the new pages. Docker required for database and API tests.

**Target Platform**: Local development with Aspire on Windows/macOS/Linux with Docker or Podman; unchanged Entra setup.

**Project Type**: Web application (Blazor Web App UI + minimal API + PostgreSQL), Aspire-orchestrated.

**Performance Goals**: First page of any list < 2 s with 500 entries (SC-004), served by one composite index per table. The Maintenance tab makes one list call per section list (five including service records), each a page of at most 10 rows.

**Constraints**: Owner derived only from validated token claims; every statement proves ownership through `vehicles.owner_id` and the vehicle id in the route, including access by entry id; DTOs only across the API boundary; parameterized SQL in `.sql` files only; no entry contents in logs or URLs; money limited to 2 decimals and 0 to 9,999,999.99; unit price up to 4 decimals; amount up to 3 decimals; dates validated against UTC today where the event has already happened.

**Scale/Scope**: Tens of vehicles per user; up to hundreds of entries per kind per vehicle; 25 new API endpoints (list, create, get, update, delete for each of five kinds); 10 new Web pages (new and edit for each kind); one reworked accordion shell and five list sections; 1 migration.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. .NET 10 / C# only; nullable on; warnings as errors; async + `CancellationToken` | PASS | All new repository, endpoint, and client methods are async and take a `CancellationToken`. Optional values are nullable. |
| II. Feature-folder organization | PASS | New `FuelEnergyEntries/`, `Inspections/`, `Registrations/`, `Taxes/` folders in Shared, Database (plus `Scripts/<Feature>/`), Api, and Web, mirrored in each test project. The accordion shell gets its own `Maintenance/` folder in Web; shared helpers go to `Common/`. |
| III. Solution structure & dependency direction | PASS | No new projects or references. Web talks to Api over HTTP only; Api uses repositories; Database references nothing. |
| IV. DTO boundary | PASS | New DTOs and enums in `MyGarage.Shared`; database records stay internal; explicit mappers with tests. |
| V. Validated minimal APIs | PASS | One `MapGroup` per feature under `/api/vehicles/{vehicleId:guid}/...`; every request DTO has a FluentValidation validator wired through `ValidationFilter<T>`; `ProblemDetails` for 400/401/403/404; OpenAPI metadata declared. |
| VI. Dapper, PostgreSQL, SQL scripts | PASS | All SQL in `Scripts/<Feature>/`, parameterized, loaded by the existing script store; repositories in Database. |
| VII. Versioned migrations | PASS | `0011_create_maintenance_records.sql`, forward-only; 0001-0010 untouched. |
| VIII. Test-first, real dependencies | PASS | Failing tests first for migration, repositories, validators, calculator, mappers, endpoints (success, validation failure, not found, 401, cross-user), and Web components/pages. |
| Auth: Entra OIDC + JWT; every endpoint authorized | PASS | All new endpoints and pages inherit the fallback policy; none is anonymous. |
| Observability: Serilog + OpenTelemetry | PASS | Logs carry ids, counts, and outcomes only; no entry contents. |
| Data protection: ownership from claims; SQL scoped by owner; child records verified via parent vehicle | PASS | No `owner_id` on the new tables; every statement joins `vehicles` with `owner_id = @OwnerId` plus the route's vehicle id. `created_by`/`updated_by` record the authenticated owner. The constitution's data-protection text names service records only; these five kinds follow the identical rule (see Complexity Tracking for a clarifying amendment note). |
| Dependencies justified | PASS | None added. |

**Post-design re-check (after Phase 1)**: unchanged; no violations. Two review notes are under Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/006-maintenance-record-types/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── api.openapi.yaml      # fuel-energy-entries, inspections, registrations, taxes
│   └── web-routes.md         # Maintenance tab sections, add and edit pages
├── checklists/
│   └── requirements.md
└── tasks.md                  # created by /speckit-tasks
```

### Source Code (repository root)

```text
docs/ui-standard.md                     # Maintenance accordion, list-row and flat-entry form patterns

src/
├── MyGarage.Database/
│   ├── FuelEnergyEntries/              # NEW: FuelEnergyEntryRepository, FuelEnergyEntryRecord
│   ├── Inspections/                    # NEW: InspectionRepository, InspectionRecord
│   ├── Registrations/                  # NEW: RegistrationRepository, RegistrationRecord
│   ├── Insurance/                      # NEW: InsurancePolicyRepository, InsurancePolicyRecord
│   ├── Taxes/                          # NEW: TaxRepository, TaxRecord
│   └── Scripts/
│       ├── Migrations/                 # + 0011_create_maintenance_records.sql
│       ├── FuelEnergyEntries/          # VehicleExists, ListPage, Count, Insert, GetById, Update, Delete
│       ├── Inspections/                # same seven scripts
│       ├── Registrations/              # same seven scripts
│       ├── Insurance/                  # same seven scripts
│       └── Taxes/                      # same seven scripts
├── MyGarage.Api/
│   ├── Program.cs                      # + Add<Feature>() and Map<Feature>Endpoints() for each feature
│   ├── Common/                         # MoneyRules moves here from ServiceRecords/ (shared by five features)
│   ├── FuelEnergyEntries/              # NEW: FuelEnergyEntriesEndpoints, FuelEnergyEntryRequestValidator, FuelEnergyEntryMapper, FuelEnergyTotal
│   ├── Inspections/                    # NEW: InspectionsEndpoints, InspectionRequestValidator, InspectionMapper
│   ├── Registrations/                  # NEW: RegistrationsEndpoints, RegistrationRequestValidator, RegistrationMapper
│   ├── Insurance/                      # NEW: InsuranceEndpoints, InsurancePolicyRequestValidator, InsurancePolicyMapper
│   └── Taxes/                          # NEW: TaxesEndpoints, TaxRequestValidator, TaxMapper
├── MyGarage.Shared/
│   ├── FuelEnergyEntries/              # NEW: FuelEnergyEntryDto, FuelEnergyEntryRequest, FuelEnergyKind
│   ├── Inspections/                    # NEW: InspectionDto, InspectionRequest, InspectionType
│   ├── Registrations/                  # NEW: RegistrationDto, RegistrationRequest, UsStates
│   ├── Insurance/                      # NEW: InsurancePolicyDto, InsurancePolicyRequest
│   └── Taxes/                          # NEW: TaxDto, TaxRequest, TaxType, TaxRecurrence
└── MyGarage.Web/
    ├── Program.cs                      # + typed HttpClient registration per new client
    ├── Common/                         # DisplayFormat (Money, Date, Number, NotSet) extracted from ServiceRecordFormat
    ├── Maintenance/                    # NEW: MaintenanceView.razor (accordion shell, moved from ServiceRecords/), FuelSection and ComplianceSection (load the lists and render headings with counts), EntryListBlock (shared list chrome), MaintenanceLinks
    ├── ServiceRecords/                 # MaintenanceView body becomes ServiceRecordsSection.razor
    ├── FuelEnergyEntries/              # NEW: FuelEnergyEntriesApiClient, FormModel, FuelEnergyEntryForm, Create, Edit, FuelEnergyEntryList (display only), Format
    ├── Inspections/                    # NEW: same set
    ├── Registrations/                  # NEW: same set
    ├── Insurance/                      # NEW: same set (InsuranceApiClient, InsurancePolicyForm, InsurancePolicyList)
    ├── Taxes/                          # NEW: same set
    └── Vehicles/                       # VehicleDetail (section and page query values, delete text); Faq and Home copy

tests/
├── MyGarage.Database.Tests/            # + Common/ (migration 0011), FuelEnergyEntries/, Inspections/, Registrations/, Insurance/, Taxes/
├── MyGarage.Api.Tests/                 # + the same five folders (validators, mappers, endpoints, cross-user)
└── MyGarage.Web.Tests/                 # + Maintenance/ (shell, sections), the five feature folders (forms, pages), Components/ (UI conformance)
```

**Structure Decision**: Extend the existing multi-project solution without new projects. Each kind is a feature of its own (own table, repository, endpoints, DTOs, and pages) attached to a vehicle by route and foreign key, following the service-records pattern for ownership through the parent vehicle. The five kinds are flat, so each is much smaller than service records: seven SQL scripts, five endpoints, two pages.

## Complexity Tracking

No constitution violations. Notes for review:

| Note | Reason |
|---|---|
| Five near-identical feature folders instead of one generic "entry" abstraction | The constitution organizes by feature, and the kinds differ in fields, validation, and labels. A generic repository would need dynamic SQL or reflection, which the "plain `.sql` files" rule discourages. Duplication is limited to small, mechanical files; shared rules (money, paging, ownership helper patterns) are reused from `Common/`. |
| The constitution's data-protection paragraph names service records only | The five new kinds follow the same parent-vehicle ownership rule. A PATCH-level amendment can generalize "service records" to "vehicle records" at merge time; no code depends on it. |
| `MaintenanceView.razor` moves from `ServiceRecords/` to `Maintenance/` | The shell now hosts five features; leaving it in `ServiceRecords/` would make that feature depend on five others. The service-records body becomes `ServiceRecordsSection.razor`, so existing behavior and tests are preserved. |

## Open points for the product owner (defaults chosen; easy to change)

1. **Entry date on fuel and charging**: the request did not list a date, but entries cannot be ordered or placed in time without one; it is required (R6).
2. **Total left empty**: calculated as price x amount, rounded to the cent; a total entered is kept as is, since pumps round and sessions carry fees (R6).
3. **No odometer on fuel and charging entries**: not requested, so the garage mileage does not change; an optional odometer can be added later (R6).
4. **Taxes have no date**: ordered by when added (R9). Say if a date is wanted.
5. **"Bi-Annual"** is taken as twice a year (R9).
6. **Insurance** holds only a start date, an end date, a duration, and a cost, as requested. Duration is how often the premium is paid (Monthly, Bi-Annual (twice a year), or Annual) and is not cross-checked against the dates; the cost is the premium paid each time, not the policy total (R9a). Its list is placed between Registrations and Taxes in the third accordion item, whose title now reads "Inspections, Registrations, Insurance, and Taxes".
7. **Next Due Date on inspections** is optional; the registration Expiration Date is required (R7, R8).
8. **State** is a pick-list of the 50 states and DC (R8).
9. **Not built**: past-due or expired badges, reminders, spending totals, fuel economy (spec Assumptions).
