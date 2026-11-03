# Research: Charging and Fueling, Inspections, Registrations, Insurance, and Taxes

All technical context was resolved from the existing code base (feature 005 in particular) and the product owner's field lists; there were no open `NEEDS CLARIFICATION` items. Each decision follows the patterns already in the solution.

## R1. Feature placement

- **Decision**: Five new top-level feature folders (`FuelEnergyEntries/`, `Inspections/`, `Registrations/`, `Insurance/`, `Taxes/`) in `MyGarage.Shared`, `MyGarage.Database` (plus `Scripts/<Feature>/`), `MyGarage.Api`, and `MyGarage.Web`, mirrored in each test project. The accordion shell moves to `MyGarage.Web/Maintenance/`; the service-records list becomes `ServiceRecords/ServiceRecordsSection.razor`.
- **Rationale**: Constitution II requires feature folders. The kinds differ in fields, rules, and labels, so each is its own small feature. The shell now composes five features, so it belongs to none of them.
- **Alternatives**: One `MaintenanceRecords/` feature with a generic record. Rejected: it would need dynamic SQL or reflection, against the plain-`.sql`-files rule, and would blur validation. Leave the shell in `ServiceRecords/`. Rejected: that feature would depend on five others.

## R2. Ownership enforcement

- **Decision**: The new tables carry no `owner_id`. Every statement joins `vehicles` and filters `vehicles.owner_id = @OwnerId` together with the `vehicleId` from the route, including statements that address an entry by id (as `ServiceRecords/*.sql` do). An entry reached through the wrong vehicle behaves as not found.
- **Rationale**: Constitution requires ownership through the parent vehicle and owner-scoped SQL; a duplicated owner column could drift.
- **Alternatives**: Denormalize `owner_id` onto each table. Rejected: extra writes and a drift risk for no benefit.

## R3. Tables and migration

- **Decision**: One table per kind (`fuel_energy_entries`, `inspections`, `registrations`, `insurance_policies`, `taxes`), each with `vehicle_id ... ON DELETE CASCADE` (so deleting a vehicle removes them, FR-013) and the standard audit columns (`created_at`, `updated_at`, `created_by`, `updated_by`). All five are created in one forward-only migration, `0011_create_maintenance_records.sql`, with one composite index per table for its list order.
- **Rationale**: The five tables ship together and are independent of each other; one migration applies atomically and keeps the ledger short. Applied scripts 0001-0010 are not edited.
- **Alternatives**: Four migrations. Rejected: no benefit when they ship as one feature.

## R4. Choice lists (enumerations)

- **Decision**: Closed lists (fuel/energy kind, inspection type, tax type, recurrence) are enums in `MyGarage.Shared` with `JsonStringEnumConverter` (like `OwnershipType`), stored as lowercase text (`fuel`, `energy`, `safety`, `emissions`, `personal_property`, `lease`, `purchase`, `once`, `monthly`, `bi_annual`, `annual`) with a `CHECK ... IN (...)` constraint, and converted explicitly in each mapper. The registration State is a fixed list of the 50 states and DC kept in `MyGarage.Shared` (`UsStates`), stored as the two-letter postal code (`char(2)`, checked to be two capital letters), and validated against the list.
- **Rationale**: These lists are product decisions, not user-managed data, so lookup tables (feature 004) would be over-engineering; a check constraint protects the data if a validator is ever bypassed. Explicit mapping keeps stored values independent of enum names.
- **Alternatives**: Lookup tables. Rejected: no administration need. Integer-coded enums. Rejected: unreadable in the database and fragile on reordering. Database check listing all 51 states. Rejected: duplicates the list in SQL; the validator owns it.

## R5. Fuel and charging as one table

- **Decision**: A single `fuel_energy_entries` table with `kind` (`fuel` or `energy`), `entry_date`, `unit_price`, `quantity`, and `total_cost`. For `fuel` the unit price is per gallon and the quantity is gallons; for `energy` they are per kWh and kWh delivered. One endpoint group serves both.
- **Rationale**: The two kinds have the same shape and appear in one list ("Charging and Fueling"); only labels differ. One table gives one ordered, paged list with no union query.
- **Alternatives**: Separate tables and lists. Rejected: two lists the user then has to interleave mentally, double the code, no difference in data.

## R6. Fuel and charging rules

- **Decision**:
  - **Date** required, between 1900-01-01 and today (UTC), as for other past events.
  - **Unit price** 0 to 9,999.9999 with at most 4 decimals (so a free charging session is allowed and per-kWh rates like 0.1342 fit).
  - **Quantity** 0.001 to 99,999.999 with at most 3 decimals (pump and charger displays).
  - **Total** optional on input, 0 to 9,999,999.99 with at most 2 decimals. When omitted, the Api computes `round(unit_price x quantity, 2)` (`MidpointRounding.AwayFromZero`) in `FuelEnergyTotal`; the request is rejected if that result would exceed the maximum. A total entered is stored as entered and is not cross-checked against price x quantity.
  - The stored `total_cost` is therefore always present.
  - No odometer reading is captured, so the garage mileage is unchanged.
- **Rationale**: The request lists price, amount, and total for both kinds. Pumps round and charging sessions add fees or idle charges, so a strict equality check would reject real receipts; a blank total saves typing. The date is needed to order and place entries in time (flagged to the owner). The odometer was not requested, so the effective-mileage rules of feature 005 stay untouched.
- **Alternatives**: Require all three values. Rejected: more typing for no gain. Always compute the total. Rejected: cannot match a receipt that includes fees. Validate within a tolerance. Rejected: arbitrary and still wrong for session fees.

## R7. Inspection rules

- **Decision**: **Type** is Safety or Emissions (required). **Date** is required, between 1900-01-01 and today (UTC). **Cost** is required, 0 to 9,999,999.99 with at most 2 decimals (0 allowed). **Next Due Date** is optional; when present it is not earlier than the inspection date and not after 2100-12-31 (also a table check constraint). Ordered by inspection date descending, ties newest created first.
- **Rationale**: Matches the request (Type, Date, Cost, Next Due Date). Some inspections have no following due date, so the date is optional; the upper bound guards against typos.
- **Alternatives**: Require Next Due Date. Rejected: blocks recording an inspection with no recurrence. Badges or reminders for due dates. Rejected: not requested.

## R8. Registration rules

- **Decision**: **State** is required (R4). **Cost** is required, 0 to 9,999,999.99 with at most 2 decimals. **Expiration Date** is required, between 1900-01-01 and 2100-12-31, in the past or the future (past registrations are history). Ordered by expiration date descending, ties newest created first.
- **Rationale**: Matches the request (State, Cost, Expiration Date). Users will record both the current and earlier registrations.
- **Alternatives**: Free-text state. Rejected: inconsistent values. Include territories and Canadian provinces. Rejected: not requested; the list can grow later in one place.

## R9. Tax rules

- **Decision**: **Type** is Personal Property, Lease, or Purchase; **Amount** is required, 0.01 to 9,999,999.99 with at most 2 decimals; **Recurrence** is Once, Monthly, Bi-Annual, or Annual. No date column; the list is ordered by when each tax was added (newest first). "Bi-Annual" means twice a year. Insurance is its own kind (R9a), not a tax type.
- **Rationale**: Built exactly as specified. The "Bi-Annual" reading is surfaced in the plan's open points rather than guessed at in the schema.
- **Alternatives**: Add a date now. Rejected: unrequested; a schema change that is cheap to make later when confirmed.

## R9a. Insurance rules

- **Decision**: Insurance is its own kind with exactly four values: **Start Date** and **End Date** (both required, each 1900-01-01 to 2100-12-31, past dates allowed, end date later than start date), **Duration** (required, how often the premium is paid: Monthly, Bi-Annual, or Annual), and **Cost** (required, 0 to 9,999,999.99, at most 2 decimals, the premium paid each time, not the policy total). Nothing else is stored. Duration is chosen by the user and is not cross-checked against the span of the dates (a monthly-paid policy still runs for a year), and no total is calculated. Ordered by `end_date DESC`, ties newest created first. No check against other insurance entries (overlaps and gaps are accepted).
- **Rationale**: The owner asked that insurance "only include" a start date, an end date, a duration, and a cost, and that duration be monthly, biannual, or annual, then clarified that duration means how often the premium is paid. So Duration is a closed list, stored like the other choice lists (R4) as lowercase text with a check constraint (`monthly`, `bi_annual`, `annual`). A choice list can be validated and sorted without free-text parsing. "Bi-Annual" means twice a year, as for taxes. A separate kind, not a tax type, because it has different fields and is not a tax.
- **Alternatives**: A whole number of months (1-120). Rejected: the owner named three choices, and the meaning is payment frequency, not term length. Storing a policy total or computing a per-period amount from the dates. Rejected: the owner listed only start date, end date, duration, and cost.

## R10. API shape

- **Decision**: One endpoint group per feature, each with list (paged), create, get, update, and delete:
  `/api/vehicles/{vehicleId:guid}/fuel-energy-entries`, `/inspections`, `/registrations`, `/insurance`, and `/taxes`, with `/{entryId:guid}` for the single-item operations. Create returns 201 with a `Location` and the DTO; update returns 200 with the DTO; delete returns 204. All errors are `ProblemDetails`; a vehicle or entry that is not the caller's is 404. Request DTOs use nullable members (so "missing" is a validation error), and a single DTO per kind serves lists and single reads.
- **Rationale**: Mirrors the service records endpoints (verbs, statuses, validation filter, not-found rules) so the code and tests stay uniform. The entries are flat, so no sub-resources or summary DTO are needed.
- **Alternatives**: A combined `/maintenance` endpoint. Rejected: constitution calls for per-feature groups.

## R11. Paging and ordering

- **Decision**: Reuse `PagingQuery`, `PagedResultDto<T>`, and the `Pager` component, default page size 10 (maximum 50), with the same out-of-range clamping as service records. Order: fuel and charging by `entry_date DESC`; inspections by `inspection_date DESC`; registrations by `expiration_date DESC`; insurance by `end_date DESC`; taxes by `created_at DESC`; every list breaks ties with `created_at DESC, id DESC`. Each table has a matching composite index `(vehicle_id, <order column> DESC, created_at DESC, id DESC)`.
- **Rationale**: Proven pieces; the index serves list, count, and the "latest date" read from the first row of page 1. SC-004 (500 entries in under 2 seconds) is met by index-ordered reads.
- **Alternatives**: Load all entries. Rejected by spec edge cases.

## R12. Maintenance tab layout and section state

- **Decision**: `Maintenance/MaintenanceView.razor` renders the existing Bootstrap accordion with three items: **Service Records** (the existing body in `ServiceRecordsSection`), **Charging and Fueling** (`FuelEnergyEntriesSection`), and **Inspections, Registrations, Insurance, and Taxes** (`InspectionsSection`, `RegistrationsSection`, `InsuranceSection`, `TaxesSection` stacked, each with its own heading, add button, list, and pager). Items still open and close independently (no `data-bs-parent`). A new query value `section` (`records` default, `fuel`, `compliance`) decides which items start expanded; create, edit, and delete pages redirect to `/vehicles/{id}?tab=service-records&section=<x>`. Each list has its own page query value (`recordsPage` as today, plus `fuelPage`, `inspectionsPage`, `registrationsPage`, `insurancePage`, `taxesPage`), read as text so a bad value means page 1. Item headings show counts (and the latest date for fuel and charging) taken from `TotalCount` and the first row of page 1, as service records do.
- **Rationale**: Keeps the accordion the user asked to keep, avoids a summary endpoint, and uses only existing Bootstrap behavior (no new JavaScript). The `section` value fixes the "returned to a collapsed accordion" problem when a user finishes an edit in a lower section.
- **Alternatives**: Tabs inside the Maintenance tab. Rejected: the user asked to keep the accordion. Persist open state in the browser. Rejected: needs JavaScript. A summary endpoint. Rejected: duplicates data the list already returns.

## R13. Flat entry pages and the add action for fuel or charging

- **Decision**: Each kind has a Create page and an Edit page (no detail page, since a row shows every value). The Edit page holds the Delete action using the existing `<details>` confirmation panel. For fuel and charging, the section has two add buttons, "Add fuel" and "Add charge", linking to the same create page with `?kind=fuel` or `?kind=energy` (read as text; anything else means fuel). The form shows the kind as read-only text and labels the fields for it (price per gallon, total gallons, total; or price per kWh, kWh delivered, total session cost); the kind is posted with the form.
- **Rationale**: The request words the two kinds differently; with static server rendering and no JavaScript the labels cannot change while the user types, so the kind is chosen before the form opens. Rows showing all values remove the need for a detail page and keep the page count at eight.
- **Alternatives**: A kind selector on the form with generic labels. Rejected: loses the requested wording. Interactive component for live labels. Rejected: breaks the static page pattern for one small benefit.

## R14. Shared helpers

- **Decision**: `MoneyRules` moves from `Api/ServiceRecords/` to `Api/Common/` (shared by five validators). `Money`, `Date`, `Number`, and `NotSet` move from `ServiceRecordFormat` to a new `Web/Common/DisplayFormat`; `ServiceRecordFormat` keeps its service-specific members and uses it. The dependency-injected `TimeProvider` supplies "today" in validators, as in feature 005.
- **Rationale**: Constitution II: cross-feature code goes in `Common/`. Avoids five features depending on `ServiceRecords/`.
- **Alternatives**: Copy the helpers. Rejected: five copies of the money and date formatting rules.

## R15. Testing

- **Decision**: Test-first per the constitution: migration test (empty database, tables, checks, cascade, idempotent re-run); per-kind repository tests (owner isolation, wrong-vehicle access, ordering, paging, round trip, cascade); validator tests (boundaries, decimals, dates); `FuelEnergyTotal` and mapper tests; endpoint tests (success, validation failure, not found, 401, cross-user, cross-vehicle); Web tests (accordion shell and section state, lists and empty states, forms and labels per kind, edit and delete confirmation, UI conformance).
- **Rationale**: Constitution VIII; real PostgreSQL through Testcontainers.
- **Alternatives**: None permitted for database tests.

## R16. Dependencies

- **Decision**: No new packages, projects, or infrastructure.
- **Rationale**: Everything needed (Dapper, Npgsql, FluentValidation, Bootstrap) is already present.
