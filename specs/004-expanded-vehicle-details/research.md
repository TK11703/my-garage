# Research: Expanded Vehicle Details and Lookup Preferences

No `NEEDS CLARIFICATION` items remain; the decisions below settle the design choices the spec left to planning.

## R1. Lookup storage model

- **Decision**: One table `lookup_items` holds all four lookups, distinguished by a `kind` column (`vehicle_type`, `fuel_type`, `transmission`, `drivetrain`). Default items have `owner_id IS NULL`; a user's own items have `owner_id` set. A second table `lookup_item_hidden (owner_id, item_id)` records items a user has hidden; no row means shown. A user sees rows where `owner_id IS NULL OR owner_id = @OwnerId`.
- **Rationale**: Defaults are shared (not copied per user), so a default added later reaches every user (FR-013) and a fresh user needs no setup (SC-002). Storing only the user's deviations (hidden rows, custom items) keeps writes tiny and makes show/hide idempotent. One table and one repository serve four lists that behave identically.
- **Alternatives**: Four tables (duplicate SQL and repository code for no behavioral difference); copy all defaults into each user's rows on first sign-in (needs a provisioning step, drifts from later default changes, complicates sign-in); hard-coded C# enums (cannot be user-extended).

## R2. Defaults and how they evolve

- **Decision**: Defaults are inserted by migration `0005_create_lookup_items.sql` with fixed UUIDs and a `sort_order` (listed order in the spec: Vehicle Types Sedan, SUV, Coupe, Hatchback, Convertible, Truck, Van, Wagon, Minivan, Crossover, Motorcycle; Fuel Types Gasoline, Diesel, Electric, Hybrid; Transmissions Automatic, Manual; Drivetrains FWD, RWD, AWD, 4WD). Later defaults are added by a new migration. A migration that adds a default whose name equals some user's custom item must in the same script repoint the vehicles using the custom item to the new default and remove the custom item (and its hidden row), so no user ever sees two items with one name.
- **Rationale**: Constitution VII makes migrations the only way to change stored data shape and content; fixed ids make tests and cross-environment data stable.
- **Alternatives**: Seeding at application startup from code (bypasses the ledger/checksum); configuration file defaults (not testable against an empty database).

## R3. Vehicle references to lookup items

- **Decision**: `vehicles` stores `vehicle_type_id`, `fuel_type_id`, `transmission_id`, `drivetrain_id` as nullable uuid foreign keys to `lookup_items(id)` (`ON DELETE RESTRICT`; items are never deleted in v1). `VehicleService` verifies before insert/update that each supplied id exists, has the matching kind, and is a default or owned by the caller; a hidden item is accepted only when it equals the value already stored on that vehicle. Violations return a validation `ProblemDetails` on that field (HTTP 400).
- **Rationale**: Satisfies FR-011/FR-012 (hidden items keep working on the vehicle but are not newly selectable; stored by reference, not copied text) and prevents referencing another user's custom item. The database cannot cheaply express "kind matches column" and "owned by caller" in a plain foreign key, so those rules live in the Api service alongside the owner-scoped repository call.
- **Alternatives**: Composite FK on `(id, kind)` with generated constant columns (clever, hard to read and migrate); storing the item's name on the vehicle (breaks consistency, cannot rename or merge later); validating inside SQL with `INSERT ... SELECT ... WHERE EXISTS` (zero-row results cannot report which field was wrong).

## R4. Adding custom items: uniqueness and limits

- **Decision**: `POST /api/lookups/{kind}` runs in one transaction. It takes `pg_advisory_xact_lock(hashtext(@OwnerId || ':' || @Kind))` (same serialization idea as the existing `LockVehicle.sql` scripts), rejects when a default or the owner's item with the same `lower(trim(name))` already exists (409, reporting whether the existing one is hidden), rejects when the owner already has 50 custom items of that kind (409), then inserts. Partial unique indexes back this up: `(kind, name_key) WHERE owner_id IS NULL` and `(owner_id, kind, name_key) WHERE owner_id IS NOT NULL`.
- **Rationale**: Matches FR-009 and the existing tag-limit pattern; the lock prevents two concurrent requests from passing the limit; unique indexes are the safety net.
- **Alternatives**: Count-and-insert without a lock (races); a global unique index across owners (would reveal other users' item names and block legitimate names).

## R5. Lookup API shape

- **Decision**: Group `/api/lookups`:
  - `GET /api/lookups?includeHidden=false|true` returns all four lists for the caller in one response (`LookupsDto`), each item `{ id, name, isDefault, isHidden }`, ordered defaults by `sort_order` then custom items by `lower(name)`. The vehicle forms call it with the default (`includeHidden=false`); the Preferences page uses `includeHidden=true`.
  - `POST /api/lookups/{kind}` body `{ name }` -> 201 `LookupItemDto` (400 validation, 404 unknown kind, 409 duplicate/limit).
  - `PUT /api/lookups/{kind}/{itemId}/visibility` body `{ isHidden }` -> 200 `LookupItemDto`; idempotent; 404 when the item does not exist, has a different kind, or belongs to another user.
  - `kind` is a route slug: `vehicle-types`, `fuel-types`, `transmissions`, `drivetrains` (constants in `MyGarage.Shared.Lookups.LookupKinds`); an unknown slug is 404.
- **Rationale**: One read call per page keeps the vehicle forms and the Preferences page to a single round trip; PUT is idempotent for show/hide; slugs read well in routes and avoid exposing the storage column values. No delete and no rename in v1 (spec assumption).
- **Alternatives**: One endpoint per lookup (four times the contract for identical behavior); PATCH with partial bodies (nothing partial to send); enum route binding (slugs with hyphens do not bind to enum names cleanly).

## R6. New vehicle fields and validation (consolidated)

| Field | Stored as | Rule |
|---|---|---|
| `nickname` (UI label "Name") | varchar(100) | required, 1-100 (unchanged) |
| `vin` | varchar(17) | optional; trimmed; stored upper case; exactly 17 characters of `[A-HJ-NPR-Z0-9]` |
| `make`, `model` | varchar(50) | required (unchanged) |
| `trim` | varchar(50) | optional (unchanged) |
| `year` | smallint, nullable | required in create and update; 1900 to (current UTC year + 1); nullable column only so pre-existing rows are valid |
| `vehicle_type_id` | uuid FK | optional; kind `vehicle_type`; available to caller (R3) |
| `ownership` | text | optional; `purchased` or `leased` (`OwnershipType` enum in the DTO, explicit mapping) |
| `purchased_on` | date | optional; not after today (UTC) |
| `lease_ends_on` | date | optional; only when ownership is leased; not before `purchased_on` when both are set |
| `engine` | varchar(100) | optional free text |
| `horsepower` | integer | optional; 1-3,000 |
| `fuel_type_id`, `transmission_id`, `drivetrain_id` | uuid FK | optional; matching kind; available to caller |
| `mileage` | integer | optional; 0-9,999,999 (unchanged) |
| `color` | varchar(30) | optional (unchanged) |
| `seats` | smallint | optional; 1-20 |
| `doors` | smallint | optional; 1-6 (unchanged) |
| `wheel_size` | numeric(3,1) | optional; 10.0-30.0 inches, at most one decimal place |
| `tire_size` | varchar(20) | optional free text |
| lookup item `name` | varchar(30) | 1-30 after trim, unique ignoring case per owner and kind, up to 50 custom per owner and kind |

All text is trimmed, blank optional text is stored as null, control characters are rejected in single-line fields, and values are output-encoded. Database `CHECK` constraints repeat the hard limits and the lease rules as a safety net; the date-relative and kind/availability rules live in the validator and service because they depend on the current date and the caller.

## R7. Existing vehicles and the required Year

- **Decision**: `year` is nullable in the database (existing rows have none) but required by `VehicleRequestValidator` on both create and update. `VehicleDetailDto.Year` and `VehicleSummaryDto.Year` are nullable; the Web detail page shows an "add a year" prompt linking to the edit form when it is null, and cards omit the year when null. Mileage, color, and doors keep their columns; only the tab they are shown on changes.
- **Rationale**: FR-002 and FR-017: new and edited vehicles always have a year, old vehicles still list and open. No backfill is invented.
- **Alternatives**: Backfilling a placeholder year (invents data); making year optional (contradicts the required-field decision).

## R8. Search and filtering additions

- **Decision**: Extend `Scripts/Vehicles/ListPage.sql` and `Count.sql` (same optional-parameter style as before, always `v.owner_id = @OwnerId`):
  - Search `q` also matches `v.year::text`, `v.vin`, and `v.engine` with the existing escaped `ILIKE @Pattern ESCAPE '\'` contains pattern.
  - New filter parameters `@MinYear`, `@MaxYear` (inclusive bounds; vehicles without a year never match a year bound) and `@VehicleTypeId`, `@FuelTypeId`, `@TransmissionId`, `@DrivetrainId` (id equality).
  - Result rows add `year`.
- **Rationale**: Same proven approach as research R16 of feature 002; id-based filters stay exact and independent of display text, including items the user later hid.
- **Alternatives**: Filtering by item name (ambiguous if a user adds a similarly named item; slower to reason about); full-text indexes (unneeded at tens of vehicles per owner).

## R9. Filter options

- **Decision**: `GET /api/vehicles/filter-options` additionally returns `vehicleTypes`, `fuelTypes`, `transmissions`, and `drivetrains` as `LookupValueDto { id, name }` lists containing only items used by the caller's own vehicles (including hidden ones), plus existing makes, colors, and tags. The min/max year inputs are plain number inputs.
- **Rationale**: FR-020 requires choices to be only values present in the user's garage; selecting by id matches R8.

## R10. Preferences page, account menu, and tabs

- **Decision**: `AppNavbar` account menu gets a "Preferences" link (`/preferences`) between "My vehicles" and the divider. `/preferences` is a static-SSR, `[Authorize]` page (like the vehicle pages, per feature 002 R11, because token acquisition needs `HttpContext`) with a page header (trail Home > Preferences), an account details card (display name and email from `GET /api/users/me`, read-only), and a tabbed card with Vehicle Types, Fuel Types, Transmissions, and Drivetrains. Each tab lists items with a badge for default/custom and shown/hidden, one form per item (`PUT .../visibility` via post-redirect-get) to show or hide, and an add form (`POST /api/lookups/{kind}`). `?tab=` selects the open tab; a rejected add re-opens its tab with the message next to the field. Tab markup uses Bootstrap tabs (data attributes, no new JavaScript) and the existing `mg-tabs` styling from feature 003; the tab strip is extracted from `VehicleTabNav` into a generic `Common/TabNav.razor` that both the vehicle pages and Preferences use.
- **Rationale**: Reuses the established page pattern, avoids interactive-circuit token issues, and keeps visual consistency with the vehicle tabs the user's reference image shows (icon + label tabs with an underline on the active tab).
- **Alternatives**: Interactive Server page (needs a circuit-safe token provider, deferred in feature 002); a separate page per lookup (more navigation for four small lists); duplicating the tab markup (drift).

## R11. Vehicle forms, detail page, and card

- **Decision**:
  - Basic Info tab: Name, VIN, Make, Model, Trim, Year, Vehicle Type (select), Purchased or Leased (select), Date Purchased and Date Lease Ends (date inputs). Both date inputs are always shown; Date Lease Ends carries help text that it applies to leased vehicles only, because the static-SSR forms have no client script to toggle fields and the server validates the combination.
  - Specifications tab: Engine, Horsepower, Fuel Type, Transmission, Mileage, Color, Seats, Doors, Drivetrain, Wheel Size, Tire Size.
  - The Media and Description tabs are unchanged. The existing "first error opens its tab" behavior in `VehicleForm` and `VehicleFormFields` is extended with the new field names.
  - Selects call `GET /api/lookups` once per page; the edit form additionally includes the vehicle's current item when hidden, labeled "{name} (hidden)".
  - Detail page shows both tabs as definition lists with "Not set" for empty values; Year is shown with a prompt to add it when missing. Cards show `year make model`. Garage filters add the four selects and min/max year to the existing collapsible filter panel.
- **Rationale**: Follows the field-to-tab placement the user specified and the patterns already in `VehicleForm`, `VehicleDetail`, and `VehicleSearchCard`.

## R12. Dates and numbers

- **Decision**: `DateOnly?` in DTOs and records mapped to PostgreSQL `date`; wheel size as `decimal?` mapped to `numeric(3,1)`; horsepower, seats, doors as integers. The validator compares dates with `TimeProvider` (UTC today) so tests control the clock. Web forms use `InputDate<DateOnly?>` and `InputNumber<decimal?>`.
- **Rationale**: Date-only values must not carry time zones (project rule: `DateOnly` and `date` for date-only values); `TimeProvider` is already used by `VehicleService`.

## R13. Observability and logging

- **Decision**: No new log statements carry field values; lookup operations log kind, item id, and outcome only. A test asserts that creating a vehicle with a VIN and adding a custom item write neither value to a captured logger. VIN never appears in a URL: it is entered in a POST body, and search by VIN uses the existing `q` query parameter chosen by the user (the user's own typed text; documented in the quickstart).
- **Rationale**: FR-022 and the constitution's logging rule. Search text was already excluded from logs by feature 002.

## R14. Test strategy

- **Database.Tests**: migration tests (0005/0006 apply to an empty database, defaults present with expected names and order, existing-vehicle rows survive 0006 with null new columns, checksum drift still detected); lookup repository (list with and without hidden, custom ordering, add + duplicate against default and own items ignoring case, limit of 50, hide/show idempotency, owner isolation: another owner never sees or toggles my custom items or my hidden flags, concurrent adds respect the limit); vehicle repository (insert/update/get with every new column, check constraints, filters and search additions including literal `%`/`_`, year bounds, filter options include hidden-but-used items, owner scoping).
- **Api.Tests**: `/api/lookups` success, validation, unknown kind, 404 for another user's item, 409 duplicate/limit, 401 unauthenticated, cross-user isolation; vehicle endpoints with new fields (valid, each invalid rule, lease/ownership combinations, year required, hidden-item reuse allowed on update but not on create, foreign custom item rejected); validator and mapper unit tests; logger test for VIN and item names.
- **Web.Tests**: navbar account menu includes Preferences; Preferences page (account details, four tabs, tab selection via `?tab=`, add and show/hide forms, error re-open); vehicle form tabs and fields placement, select options (hidden current item), first-error tab opening; detail page both tabs and missing-year prompt; card year; filter panel new controls; `UiStandardConformanceTests` extended.
- **AppHost.Tests**: unchanged.

## R15. Dependencies

- **Decision**: No new third-party packages, projects, or Aspire resources.
- **Rationale**: Everything needed (Dapper, Npgsql, FluentValidation, Bootstrap tabs) is already in the solution; constitution requires justification for anything new.
