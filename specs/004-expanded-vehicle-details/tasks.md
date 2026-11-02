---

description: "Task list for Expanded Vehicle Details and Lookup Preferences"
---

# Tasks: Expanded Vehicle Details and Lookup Preferences

**Input**: Design documents from `/specs/004-expanded-vehicle-details/`

**Prerequisites**: plan.md, spec.md, research.md (R1-R15), data-model.md, contracts/ (`api.openapi.yaml`, `web-routes.md`), quickstart.md

**Tests**: Included. The constitution (VIII) requires test-first development: write each story's tests, run them, and confirm they FAIL before implementing. Repository, migration, and API tests use real PostgreSQL via Testcontainers (Docker required); the database is never mocked.

**Organization**: Tasks are grouped by user story. Paths follow the structure in plan.md. Story numbers match spec.md: US1 fuller vehicle details on the Basic Info and Specifications tabs (P1), US2 default lookups and customization (P1), US3 Preferences page (P1), US4 existing vehicles keep working (P2), US5 year on cards and search/filter (P2), US6 privacy (P3).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1-US6, only on user-story phase tasks
- Constraints from data-model.md and research.md R6 are quoted in the task that creates the artifact; do not loosen them
- This feature adds no projects, packages, or Aspire resources (research R15)

## Phase 1: Setup

**Purpose**: Confirm a clean baseline; there is no new project or package to add.

- [X] T001 On branch `004-expanded-vehicle-details`, run `dotnet build MyGarage.slnx` and confirm zero warnings, then run `dotnet test tests/MyGarage.Web.Tests` (no Docker needed) to record the baseline; confirm `src/MyGarage.Database/Scripts/Migrations/` contains exactly `0001`-`0004` (never edit them)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The schema, the lookup DTOs, and the read path for lookups. Every story needs the new columns and the seeded defaults, and the vehicle forms (US1) need the lookup list, so the read-only part of the lookups feature lives here; add/hide/show (US2) and the Preferences page (US3) build on it.

**CRITICAL**: No user story work begins until this phase is complete.

### Tests first (confirm they fail)

- [X] T002 [P] Update `tests/MyGarage.Database.Tests/Common/MigrationRunnerTests.cs`: a fresh database now ends with scripts 0001-0006 applied and ledger rows for each; tables `lookup_items` and `lookup_item_hidden` exist; seeded defaults are exactly Vehicle Types (`vehicle_type`): Sedan, SUV, Coupe, Hatchback, Convertible, Truck, Van, Wagon, Minivan, Crossover, Motorcycle; Fuel Types (`fuel_type`): Gasoline, Diesel, Electric, Hybrid; Transmissions (`transmission`): Automatic, Manual; Drivetrains (`drivetrain`): FWD, RWD, AWD, 4WD, each with `owner_id` NULL, `created_by` NULL, and `sort_order` 1..n in that order; schema assertions: `kind` CHECK rejects `'body_style'`; duplicate default `(kind, name_key)` rejected; duplicate custom `(owner_id, kind, name_key)` rejected but the same name for two different owners is allowed; `CHECK ((owner_id IS NULL) = (created_by IS NULL))` rejects a row with owner and no `created_by`; deleting a lookup item cascades its `lookup_item_hidden` rows; a vehicle row inserted after only `0001`-`0004` ran (use the runner against a database where only those scripts are applied, then apply `0005`-`0006`) survives with every new column NULL and its `nickname`, `make`, `model`, `trim`, `color`, `mileage`, `doors` unchanged; applied-script checksum drift still throws
- [X] T003 [P] `LookupRepositoryTests` in `tests/MyGarage.Database.Tests/Lookups/LookupRepositoryTests.cs` for `ListAsync(ownerId, includeHidden)`: a brand-new owner sees all 11/4/2/4 defaults in `sort_order` and no hidden items; with a `lookup_item_hidden` row inserted directly for owner A, `includeHidden: false` omits that item for A and `true` returns it with `IsHidden` true while owner B still sees it shown; owner A's custom item (inserted directly) appears after the defaults of its kind ordered by `lower(name)`, is flagged `IsDefault` false, and is never returned for owner B; each record carries its kind
- [X] T004 [P] `LookupsEndpointsTests` (read) in `tests/MyGarage.Api.Tests/Lookups/LookupsEndpointsTests.cs` using `ApiFactory`: `GET /api/lookups` 200 returns `vehicleTypes`, `fuelTypes`, `transmissions`, `drivetrains` with the defaults in order and `isDefault` true / `isHidden` false for a new user; `includeHidden=false` hides and `includeHidden=true` returns an item hidden for that user; user B never sees user A's custom items or hidden flags; no token -> 401 `application/problem+json`; a query or header carrying `ownerId`/`userId` is ignored
- [X] T005 [P] `LookupMapperTests` in `tests/MyGarage.Api.Tests/Lookups/LookupMapperTests.cs`: explicit mapping from `LookupItemRecord` to `LookupItemDto` and a flat record list to `LookupsDto` (grouped by kind in default-then-custom order, empty lists when a kind has no items); `LookupKinds` slugs `vehicle-types`, `fuel-types`, `transmissions`, `drivetrains` map to stored kinds `vehicle_type`, `fuel_type`, `transmission`, `drivetrain`; an unknown slug maps to `null`; no DTO has an owner or user id

### Implementation

- [X] T006 [P] Create `src/MyGarage.Database/Scripts/Migrations/0005_create_lookup_items.sql` per data-model.md: table `lookup_items` with `id` uuid PRIMARY KEY; `kind` text NOT NULL with `CHECK (kind IN ('vehicle_type', 'fuel_type', 'transmission', 'drivetrain'))`; `owner_id` text NULL (NULL = application default); `name` varchar(30) NOT NULL; `name_key` text NOT NULL generated always as `lower(name)` stored; `sort_order` integer NOT NULL DEFAULT 0; `created_at` timestamptz NOT NULL; `created_by` text NULL; `CHECK ((owner_id IS NULL) = (created_by IS NULL))`; unique index `ux_lookup_items_default (kind, name_key) WHERE owner_id IS NULL`; unique index `ux_lookup_items_custom (owner_id, kind, name_key) WHERE owner_id IS NOT NULL`; table `lookup_item_hidden` with `owner_id` text NOT NULL, `item_id` uuid NOT NULL REFERENCES `lookup_items(id)` ON DELETE CASCADE, `hidden_at` timestamptz NOT NULL, `PRIMARY KEY (owner_id, item_id)`; then `INSERT` the default items with hard-coded fixed UUIDs, `created_at` set to the migration date as a literal timestamp, and `sort_order` 1..n in the order listed in T002 (research R2)
- [X] T007 [P] Create `src/MyGarage.Database/Scripts/Migrations/0006_add_vehicle_details.sql` (one `ALTER TABLE vehicles` per group is fine; all new columns NULLable so existing rows stay valid): `vin` varchar(17) with `CHECK (vin ~ '^[A-HJ-NPR-Z0-9]{17}$')`; `year` smallint with `CHECK (year BETWEEN 1900 AND 2200)`; `vehicle_type_id` uuid REFERENCES `lookup_items(id)` ON DELETE RESTRICT; `ownership` text with `CHECK (ownership IN ('purchased', 'leased'))`; `purchased_on` date; `lease_ends_on` date with `CHECK (lease_ends_on IS NULL OR ownership = 'leased')` and `CHECK (lease_ends_on IS NULL OR purchased_on IS NULL OR lease_ends_on >= purchased_on)`; `engine` varchar(100); `horsepower` integer with `CHECK (horsepower BETWEEN 1 AND 3000)`; `fuel_type_id`, `transmission_id`, `drivetrain_id` uuid each REFERENCES `lookup_items(id)` ON DELETE RESTRICT; `seats` smallint with `CHECK (seats BETWEEN 1 AND 20)`; `wheel_size` numeric(3,1) with `CHECK (wheel_size BETWEEN 10 AND 30)`; `tire_size` varchar(20); do not change existing columns or the index `ix_vehicles_owner_nickname`
- [X] T008 [P] Create the lookup DTOs in `src/MyGarage.Shared/Lookups/` (one file each, no owner/user id fields): `LookupKinds` (constants `VehicleTypes = "vehicle-types"`, `FuelTypes = "fuel-types"`, `Transmissions = "transmissions"`, `Drivetrains = "drivetrains"`), `LookupItemDto` (`Id`, `Name`, `IsDefault`, `IsHidden`), `LookupsDto` (`VehicleTypes`, `FuelTypes`, `Transmissions`, `Drivetrains`, each `IReadOnlyList<LookupItemDto>`), `LookupValueDto` (`Id`, `Name`), `AddLookupItemRequest` (`Name`, `string?`), `SetLookupVisibilityRequest` (`IsHidden`, `bool?` so a missing value can be rejected by validation)
- [X] T009 Implement the read path in `src/MyGarage.Database/Lookups/`: `LookupKind.cs` (slug <-> stored kind mapping used by the Api), `LookupItemRecord.cs` (`Id`, `Kind`, `Name`, `IsDefault`, `IsHidden`, `SortOrder`); `src/MyGarage.Database/Scripts/Lookups/List.sql` (items where `owner_id IS NULL OR owner_id = @OwnerId`, left join `lookup_item_hidden h ON h.item_id = i.id AND h.owner_id = @OwnerId`, `IsHidden = h.item_id IS NOT NULL`, `(@IncludeHidden OR h.item_id IS NULL)`, ordered by `i.kind, (i.owner_id IS NOT NULL), i.sort_order, i.name_key`); `LookupRepository.ListAsync(string ownerId, bool includeHidden, CancellationToken)` using `SqlScriptStore` and Dapper; register `LookupRepository` in `src/MyGarage.Database/Common/DatabaseServiceCollectionExtensions.cs`; T002 and T003 must pass
- [X] T010 Implement the Api read path in `src/MyGarage.Api/Lookups/`: `LookupMapper.cs`, `LookupService.cs` (`GetAsync(ownerId, includeHidden, ct)`), `LookupsEndpoints.cs` (`MapLookupsEndpoints()` with `MapGroup("/api/lookups").WithTags("Lookups")`; `GET /` with `includeHidden` bool query defaulting false, `WithName("GetLookups")`, `Produces<LookupsDto>()`, `ProducesProblem` 401/403; owner from `CurrentOwner`, 403 `ProblemDetails` when absent); register the service and call `app.MapLookupsEndpoints()` in `src/MyGarage.Api/Program.cs` so T004 and T005 pass
- [X] T011 [P] Create `src/MyGarage.Web/Lookups/LookupsApiClient.cs` (typed `HttpClient` like `VehiclesApiClient`: `GetAsync(bool includeHidden, CancellationToken)` -> `ApiResult<LookupsDto>`; 401/403 -> the same not-accepted outcome, other failures -> unavailable) and register it in `src/MyGarage.Web/Program.cs` with `AddHttpClient<LookupsApiClient>(...)` using the same base address and `ApiAccessTokenHandler` as `VehiclesApiClient`
- [X] T012 [P] Update `ApiFactory.ResetDataAsync()` in `tests/MyGarage.Api.Tests/Common/ApiFactory.cs` so that after `TRUNCATE TABLE vehicles CASCADE` it also runs `DELETE FROM lookup_item_hidden` and `DELETE FROM lookup_items WHERE owner_id IS NOT NULL`, never touching the seeded defaults; add a small helper in `tests/MyGarage.Api.Tests/Common/` (for example `LookupTestData.cs`) to look up a default item id by kind and name through the API and to insert a custom item or hidden row directly for a given owner

**Checkpoint**: `dotnet test` passes T002-T005 and all pre-existing tests; migrations 0001-0006 apply to an empty database; `GET /api/lookups` returns the defaults.

---

## Phase 3: User Story 1 - Fuller vehicle details on the Basic Info and Specifications tabs (Priority: P1) MVP

**Goal**: A signed-in user adds, edits, and views a vehicle using the new fields placed on the Basic Info and Specifications tabs, with validation, required Name/Year/Make/Model, and lookup dropdowns.

**Independent Test**: Add a vehicle with every field filled (values shown on matching tabs of its detail page), then one with only Name, Year, Make, and Model (empty fields show "Not set"); try each invalid value and confirm per-field messages and that the tab with the first error opens.

### Tests for User Story 1 (write first, confirm they fail)

- [X] T013 [P] [US1] Extend `tests/MyGarage.Database.Tests/Vehicles/VehicleRepositoryTests.cs` (and `tests/MyGarage.Database.Tests/Common/TestData.cs`): insert then get round-trips every new column (values and nulls) including the four lookup item ids and the item names returned by `GetByIdAsync` (`VehicleTypeName`, `FuelTypeName`, `TransmissionName`, `DrivetrainName`); update replaces all new fields and clears them when null; a vehicle referencing an unknown lookup id fails with the foreign-key violation; CHECKs reject: `vin` of 16 characters, lower-case `vin`, `vin` containing `I`, `year` 1899 and 2201, `ownership` `'rented'`, `lease_ends_on` with `ownership` NULL or `'purchased'`, `lease_ends_on` earlier than `purchased_on`, `horsepower` 0 and 3001, `seats` 0 and 21, `wheel_size` 9.9 and 30.1; owner scoping unchanged (another owner cannot get or update the row)
- [X] T014 [P] [US1] Extend `tests/MyGarage.Api.Tests/Vehicles/VehicleRequestValidatorTests.cs` (the validator now takes a `TimeProvider`; use a fixed clock): `nickname` required 1-100 chars (trimmed); `make` and `model` required 1-50; `trim` optional <= 50; `color` optional <= 30; `engine` optional <= 100; `tireSize` optional <= 20; control characters rejected in all single-line text; `year` required (null -> error on `Year`), a whole number from 1900 to next calendar year (UTC), boundaries 1899, 1900, current year + 1, current year + 2; `vin` optional, trimmed and upper-cased before validation, exactly 17 characters of `[A-HJ-NPR-Z0-9]` (16, 18, `I`, `O`, `Q`, lower-case valid VIN accepted); `horsepower` optional 1-3,000; `seats` optional 1-20; `doors` optional 1-6; `mileage` optional 0-9,999,999; `wheelSize` optional 10 to 30 with at most one decimal place (9.9, 10, 30, 30.1, 17.55 rejected); `purchasedOn` not after today (UTC); `leaseEndsOn` allowed only when `ownership` is `Leased`, and not before `purchasedOn` when both are given; messages are specific and keyed by property name (`Vin`, `Year`, `PurchasedOn`, `LeaseEndsOn`, `Horsepower`, `Seats`, `WheelSize`)
- [X] T015 [P] [US1] Extend `tests/MyGarage.Api.Tests/Vehicles/VehicleMapperTests.cs`: `VehicleRequest` -> `VehicleRecord` for every new field (text trimmed, blank optional text -> null, VIN upper-cased, `Ownership` enum -> `'purchased'`/`'leased'`); `VehicleRecord` -> `VehicleDetailDto` for every new field with lookup references as `LookupValueDto` (id and name, or null when unset) and the enum mapped back; no owner id appears on any DTO
- [X] T016 [P] [US1] Extend `tests/MyGarage.Api.Tests/Vehicles/VehiclesEndpointsTests.cs` and `VehicleChangeEndpointsTests.cs`: `POST /api/vehicles` with all fields -> 201 and a `VehicleDetailDto` echoing every value with lookup names; with only `nickname`, `year`, `make`, `model` -> 201 and null optional fields; each invalid rule from T014 -> 400 `ValidationProblem` keyed by the right property; missing `year` -> 400 on `Year`; `PUT` replaces all new fields; a lookup id of the wrong kind (for example a fuel type id in `VehicleTypeId`), an unknown id, and another user's custom item id are each 400 on that field with the same message (no disclosure that the item exists); a default item and the caller's own custom item are accepted; a request body carrying `ownerId` is ignored
- [X] T017 [P] [US1] Update Web form tests in `tests/MyGarage.Web.Tests/Vehicles/VehicleFormTests.cs`, `VehicleCreatePageTests.cs`, and `VehicleEditPageTests.cs`: Basic Info tab holds, in order, Name*, VIN, Make*, Model*, Trim, Year*, Vehicle Type (select), Purchased or Leased (select with Not set / Purchased / Leased), Date Purchased and Date Lease Ends (date inputs, Lease Ends with help text "Leased vehicles only"); Specifications tab holds Engine, Horsepower, Fuel Type (select), Transmission (select), Mileage, Color, Seats, Doors, Drivetrain (select), Wheel size, Tire size; required fields are marked with `aria-required`; each select starts with a "Not set" option followed by the lookup items in the order returned; the edit form pre-fills every value; API 400 errors appear next to their field and the tab holding the first error is open (Basic Info or Specifications) and the entered values are kept; success redirects to the vehicle
- [X] T018 [P] [US1] Update `tests/MyGarage.Web.Tests/Vehicles/VehicleDetailPageTests.cs`: the Basic Info tab shows Name, VIN, Make, Model, Trim, Year, Vehicle Type, Purchased or Leased, Date Purchased, Date Lease Ends and the Specifications tab shows Engine, Horsepower, Fuel Type, Transmission, Mileage, Color, Seats, Doors, Drivetrain, Wheel size, Tire size as definition lists; empty values read "Not set"; lookup values show the item name; Media and Description tabs are unchanged

### Implementation for User Story 1

- [X] T019 [P] [US1] Update the vehicle DTOs in `src/MyGarage.Shared/Vehicles/`: add `OwnershipType.cs` (enum `Purchased`, `Leased`, serialized as strings with `[JsonConverter(typeof(JsonStringEnumConverter))]`); `VehicleRequest` gains `Vin?` (string), `Year?` (int), `VehicleTypeId?`, `Ownership?` (`OwnershipType`), `PurchasedOn?` (`DateOnly`), `LeaseEndsOn?` (`DateOnly`), `Engine?`, `Horsepower?` (int), `FuelTypeId?`, `TransmissionId?`, `Seats?` (int), `DrivetrainId?` (Guid), `WheelSize?` (decimal), `TireSize?`; `VehicleDetailDto` gains the same fields with `VehicleType?`, `FuelType?`, `Transmission?`, `Drivetrain?` as `LookupValueDto` and `Year?`; existing fields unchanged; no owner or user id
- [X] T020 [US1] Update the data layer in `src/MyGarage.Database/Vehicles/`: add the new properties to `VehicleRecord` (the four `...Id` values and, for reads, `VehicleTypeName`, `FuelTypeName`, `TransmissionName`, `DrivetrainName`), and update `Scripts/Vehicles/Insert.sql` and `Update.sql` to write every new column (`@Vin`, `@Year`, `@VehicleTypeId`, `@Ownership`, `@PurchasedOn`, `@LeaseEndsOn`, `@Engine`, `@Horsepower`, `@FuelTypeId`, `@TransmissionId`, `@Seats`, `@DrivetrainId`, `@WheelSize`, `@TireSize`), `Scripts/Vehicles/GetById.sql` to read them with `LEFT JOIN lookup_items` for the four names; keep `owner_id = @OwnerId` on every statement; `VehicleRepository` needs no signature change; `DateOnly` and `decimal` map through Dapper (register a `DateOnly` type handler in `src/MyGarage.Database/Common/DapperSetup.cs` if Dapper does not map it natively) so T013 passes
- [X] T021 [P] [US1] Add `LookupRepository.FindAvailableAsync(string ownerId, IReadOnlyCollection<Guid> itemIds, CancellationToken)` with `src/MyGarage.Database/Scripts/Lookups/FindAvailable.sql`: returns `LookupItemRecord` rows for the supplied ids that are a default or owned by `@OwnerId` (never another owner's), including `Kind` and `IsHidden`; extend `tests/MyGarage.Database.Tests/Lookups/LookupRepositoryTests.cs` with the matching cases (default found, own custom found, another owner's custom not found, unknown id not found, hidden item returned with `IsHidden` true)
- [X] T022 [US1] Update `src/MyGarage.Api/Vehicles/VehicleRequestValidator.cs` (constructor takes `TimeProvider`; register through DI as today) with exactly the rules in T014, using the existing `TrimmedText` helper in `src/MyGarage.Api/Common/TextRules.cs` for text fields; error messages name the field and the rule (for example "Year must be between 1900 and {next year}.", "VIN must be 17 letters and digits and cannot contain I, O, or Q.", "Date lease ends applies only to leased vehicles.")
- [X] T023 [US1] Update `src/MyGarage.Api/Vehicles/VehicleMapper.cs` for every new field per T015 (explicit mapping, VIN `Trim().ToUpperInvariant()`, `OwnershipType` <-> `'purchased'`/`'leased'`, lookup references mapped to `LookupValueDto`)
- [X] T024 [US1] Add `src/MyGarage.Api/Vehicles/VehicleLookupChecker.cs` (service, registered in DI): given the owner id and a `VehicleRequest`, calls `LookupRepository.FindAvailableAsync` once for the non-null ids and returns a field-to-message dictionary: `VehicleTypeId` must be kind `vehicle_type`, `FuelTypeId` kind `fuel_type`, `TransmissionId` kind `transmission`, `DrivetrainId` kind `drivetrain`, and each must be a default or owned by the caller; a missing, wrong-kind, or foreign item produces the same message ("The selected vehicle type is not available.") so existence is never disclosed; call it from the `POST /` and `PUT /{vehicleId}` handlers in `src/MyGarage.Api/Vehicles/VehiclesEndpoints.cs` after validation and before the service, returning `Results.ValidationProblem(errors)` (400 `ProblemDetails`) when non-empty; the hidden-item rule is added by US2 (T043); T016 must pass
- [X] T025 [US1] Update `src/MyGarage.Web/Vehicles/VehicleFormModel.cs` with `Vin`, `Year`, `VehicleTypeId`, `Ownership` (`OwnershipType?`), `PurchasedOn`, `LeaseEndsOn` (`DateOnly?`), `Engine`, `Horsepower`, `FuelTypeId`, `TransmissionId`, `Seats`, `DrivetrainId`, `WheelSize` (`decimal?`), `TireSize` mapped in `ToRequest()` and `From(VehicleDetailDto)`; update `src/MyGarage.Web/Vehicles/VehicleFormFields.cs`: `BasicFields` = Nickname, Vin, Make, Model, Trim, Year, VehicleTypeId, Ownership, PurchasedOn, LeaseEndsOn; `SpecificationFields` = Engine, Horsepower, FuelTypeId, TransmissionId, Mileage, Color, Seats, Doors, DrivetrainId, WheelSize, TireSize (used to open the tab that holds the first error); add the same property names to the `Fields` array in `VehicleForm.razor` (T028)
- [X] T026 [US1] Rewrite `src/MyGarage.Web/Vehicles/VehicleBasicFields.razor` to the Basic Info layout in order Name*, VIN, Make*, Model*, Trim, Year*, Vehicle Type, Purchased or Leased, Date Purchased, Date Lease Ends; every input labeled with an `id`, `maxlength` from the limits (Name 100, VIN 17, Make 50, Model 50, Trim 50), `aria-required` on required fields and `aria-describedby` + `ValidationMessage` per field as the existing fields do; Year is `InputNumber<int?>` with `min="1900"` and `max` = next year; selects are `InputSelect` with a leading "Not set" option fed by new parameters `VehicleTypes` (`IReadOnlyList<LookupItemDto>`-like items) and `OwnershipChoices`; Date Lease Ends has help text "Leased vehicles only"; user text is rendered encoded
- [X] T027 [US1] Rewrite `src/MyGarage.Web/Vehicles/VehicleSpecificationFields.razor` to the Specifications layout in order Engine (maxlength 100), Horsepower (`min` 1 `max` 3000), Fuel Type (select), Transmission (select), Mileage (existing, `min` 0 `max` 9999999), Color (maxlength 30), Seats (`min` 1 `max` 20), Doors (`min` 1 `max` 6), Drivetrain (select), Wheel size (in) (`InputNumber<decimal?>` `min` 10 `max` 30 `step` 0.1), Tire size (maxlength 20), with the same label/`aria-describedby`/`ValidationMessage` pattern; select parameters `FuelTypes`, `Transmissions`, `Drivetrains`
- [X] T028 [US1] Update `src/MyGarage.Web/Vehicles/VehicleForm.razor`: add the new property names to its `Fields` array, pass the lookup choices (new `Lookups` parameter of a small `VehicleFormLookups` record in `src/MyGarage.Web/Vehicles/VehicleFormLookups.cs` that also appends the vehicle's current item as "`{name}` (hidden)" when its id is not in the shown list) to `VehicleBasicFields` and `VehicleSpecificationFields` in both the tabbed and non-tabbed layouts, and keep the first-error-tab logic using the updated `VehicleFormFields` lists
- [X] T029 [US1] Update `src/MyGarage.Web/Vehicles/VehicleCreate.razor` and `VehicleEdit.razor` to load the shown lookups with `LookupsApiClient.GetAsync(includeHidden: false)` (a lookup load failure shows the existing unavailable alert and the rest of the page is not rendered), build the `VehicleFormLookups` (on edit including the stored hidden items from the loaded `VehicleDetailDto`), and pass them to `VehicleForm`; submit with the updated `VehicleFormModel.ToRequest()`; T017 must pass
- [X] T030 [US1] Update `src/MyGarage.Web/Vehicles/VehicleDetail.razor` and `VehicleFormat.cs`: Basic Info `dl` lists Name, VIN, Make, Model, Trim, Year, Vehicle Type, Purchased or Leased, Date Purchased, Date Lease Ends; Specifications `dl` lists Engine, Horsepower, Fuel Type, Transmission, Mileage, Color, Seats, Doors, Drivetrain, Wheel size, Tire size; "Not set" for empty values; add `VehicleFormat` helpers for dates (`DateOnly` in a culture-neutral readable form), horsepower ("hp"), wheel size (inches), and ownership text; keep Media and Description tabs as they are; T018 must pass

**Checkpoint**: US1 tests pass; quickstart steps 2.3-2.4 work (using only the default lookup items).

---

## Phase 4: User Story 2 - Default lookups that each user can customize (Priority: P1)

**Goal**: Every user starts with the defaults and can add their own items and hide or show any item; hidden items leave the dropdowns but keep working on vehicles that use them; users never affect each other.

**Independent Test**: As a new user check the four dropdowns hold the defaults; via the API hide a default, add a custom item and confirm the lists; confirm a second user is unaffected; confirm a vehicle keeps a now-hidden item.

### Tests for User Story 2 (write first, confirm they fail)

- [X] T031 [P] [US2] `LookupRepositoryChangeTests` in `tests/MyGarage.Database.Tests/Lookups/LookupRepositoryChangeTests.cs`: `AddCustomAsync` creates an owner-scoped item (`owner_id`, `created_by` = owner, `sort_order` 0, trimmed name) and returns it shown; duplicate against a default (including a hidden default) and against the owner's own item is reported as `Duplicate` ignoring letter case and surrounding spaces, with `ExistingIsHidden` true when the existing one is hidden for that owner; the same name for another owner is allowed; the 51st custom item of one kind for one owner is reported as `LimitReached` while another kind and another owner are unaffected; 20 concurrent adds from one owner never exceed 50 (advisory lock); `HideAsync`/`ShowAsync` are idempotent and return false for an unknown item, an item of a different kind, and another owner's custom item; hiding a default for owner A does not change owner B's list; hidden rows never reference another owner's custom item
- [X] T032 [P] [US2] `AddLookupItemRequestValidatorTests` and `SetLookupVisibilityRequestValidatorTests` in `tests/MyGarage.Api.Tests/Lookups/`: `name` required, trimmed, 1-30 characters, no control characters (boundaries 0, 1, 30, 31, spaces only); `isHidden` required (a missing value is rejected rather than treated as false)
- [X] T033 [P] [US2] Extend `tests/MyGarage.Api.Tests/Lookups/LookupsEndpointsTests.cs` (or add `LookupsChangeEndpointsTests.cs`): `POST /api/lookups/{kind}` -> 201 `LookupItemDto` (shown, not default) that then appears after the defaults in `GET /api/lookups`; 400 `ValidationProblem` for empty and 31-character names; 404 `ProblemDetails` for an unknown kind slug; 409 `ProblemDetails` for a duplicate (title/detail distinguishes "already exists" from "already exists but is hidden" and from "limit of 50 reached"); `PUT /api/lookups/{kind}/{itemId}/visibility` -> 200 with the new `isHidden`, idempotent on repeat; 404 for an unknown item id, an id of another kind, and user B hiding or showing user A's custom item (and no change happens); 401 for each endpoint without a token; hiding a default for user A leaves user B's `GET /api/lookups` unchanged
- [X] T034 [P] [US2] Extend `tests/MyGarage.Api.Tests/Vehicles/VehicleChangeEndpointsTests.cs` for hidden items: creating a vehicle that references an item the caller has hidden -> 400 on that field; updating a vehicle that already stores a now-hidden item, leaving that field unchanged -> 200 and the item still appears in the vehicle's detail; updating it to a different hidden item -> 400; hiding an item never alters any stored vehicle (read it back); the vehicle detail still returns the hidden item's id and name
- [X] T035 [P] [US2] Add a test to `tests/MyGarage.Database.Tests/Lookups/LookupRepositoryChangeTests.cs` for FR-013: after seeding a new default row directly (`owner_id` NULL, new fixed id, next `sort_order`) in a fresh database, a user who never touched the lists sees it in `ListAsync` as shown, and a user who had hidden other items still sees it shown

### Implementation for User Story 2

- [X] T036 [P] [US2] Create the lookup SQL in `src/MyGarage.Database/Scripts/Lookups/` (all parameterized, every statement owner-checked): `LockOwnerKind.sql` (`SELECT pg_advisory_xact_lock(hashtext(@OwnerId || ':' || @Kind))`), `FindByName.sql` (an item of `@Kind` with `name_key = lower(@Name)` that is a default or owned by `@OwnerId`, returning whether `@OwnerId` has it hidden), `CountCustom.sql`, `Insert.sql` (`owner_id` = `@OwnerId`, `created_by` = `@OwnerId`, `sort_order` 0), `GetVisibleItem.sql` (item by id and kind that is a default or owned by `@OwnerId`), `Hide.sql` (`INSERT ... ON CONFLICT DO NOTHING` into `lookup_item_hidden` with `@OwnerId`, `@ItemId`, `@HiddenAt`), `Show.sql` (`DELETE ... WHERE owner_id = @OwnerId AND item_id = @ItemId`)
- [X] T037 [US2] Implement in `src/MyGarage.Database/Lookups/LookupRepository.cs`: `AddCustomAsync(ownerId, kind, name, now, ct)` returning a result (`Created(record)`, `Duplicate(existingIsHidden)`, `LimitReached`) in one transaction that takes the advisory lock, checks duplicates then the limit of 50 custom items per owner and kind, then inserts; `HideAsync(ownerId, kind, itemId, now, ct)` and `ShowAsync(ownerId, kind, itemId, ct)` returning the resulting `LookupItemRecord?` (null when `GetVisibleItem` finds no default or owned item of that kind); all take `CancellationToken`; T031 and T035 must pass
- [X] T038 [P] [US2] Implement `src/MyGarage.Api/Lookups/AddLookupItemRequestValidator.cs` (`name` rules from T032 via `TrimmedText("Name", 30, required: true)`) and `SetLookupVisibilityRequestValidator.cs` (`isHidden` required: `NotNull()` on the nullable `SetLookupVisibilityRequest.IsHidden`)
- [X] T039 [US2] Extend `src/MyGarage.Api/Lookups/LookupService.cs` and `LookupsEndpoints.cs` per research R5: `POST /{kind}` (`ValidationFilter<AddLookupItemRequest>`, 201 with `Location` omitted, 404 for an unknown slug, 409 `ProblemDetails` for `Duplicate` and `LimitReached` with the distinguishing detail text) with `WithName("AddLookupItem")`, and `PUT /{kind}/{itemId:guid}/visibility` (`ValidationFilter<SetLookupVisibilityRequest>`, 200, 404 when the repository returns null) with `WithName("SetLookupItemVisibility")`; `Produces`/`ProducesProblem` for 200/201/400/401/403/404/409; log kind, item id, and outcome only, never the item name; T032 and T033 must pass
- [X] T040 [US2] Add the hidden-item rule to `src/MyGarage.Api/Vehicles/VehicleLookupChecker.cs` (T024): on create a hidden item is rejected; on update a hidden item is accepted only when it equals the value already stored for that field on that vehicle (load the stored vehicle with `VehicleRepository.GetByIdAsync(ownerId, vehicleId)` for the comparison); pass the vehicle id from the `PUT` handler; T034 must pass
- [X] T041 [P] [US2] Extend `src/MyGarage.Web/Lookups/LookupsApiClient.cs` with `AddAsync(string kind, string name, ct)` (201 -> the new item; 400 -> errors dictionary; 409 -> a result carrying the problem detail text; 404 -> not found) and `SetVisibilityAsync(string kind, Guid itemId, bool isHidden, ct)` (200 -> the item; 404 -> not found)

**Checkpoint**: US2 tests pass; the lookup API supports add/hide/show end to end (quickstart section 3 and steps 2.6-2.7 once US3 provides the page).

---

## Phase 5: User Story 3 - Manage lookups from a Preferences page (Priority: P1)

**Goal**: The account menu has a Preferences entry opening `/preferences`, which shows read-only account details and a tabbed section with one tab per lookup where items are shown, hidden, and added.

**Independent Test**: Open the account menu, choose Preferences, check the account details, switch tabs, hide an item, add an item, and confirm both on the tab and in a vehicle form dropdown.

### Tests for User Story 3 (write first, confirm they fail)

- [X] T042 [P] [US3] `TabNavTests` in `tests/MyGarage.Web.Tests/Common/TabNavTests.cs` for the generic strip: renders `ul.nav.mg-tabs[role=tablist]` with one `button[role=tab]` per tab (icon + label), `data-bs-toggle="tab"`, `data-bs-target="#tab-{id}"`, `aria-controls`, and `aria-selected`/`active` only on the current tab; an unknown current id leaves the first tab active when the caller normalizes; and the existing `VehicleTabNav` still renders identical markup for the four vehicle tabs (guards the extraction)
- [X] T043 [P] [US3] Extend `tests/MyGarage.Web.Tests/Components/Layout/AppNavbarTests.cs` and `AppNavbarSignedOutTests.cs`: the signed-in account menu lists, in order, the display name, "My vehicles", "Preferences" linking to `/preferences`, a divider, and "Sign out"; the signed-out navbar has no Preferences link
- [X] T044 [P] [US3] `PreferencesPageTests` in `tests/MyGarage.Web.Tests/Preferences/PreferencesPageTests.cs` (bUnit with a fake `LookupsApiClient` and `GarageApiClient`): page header trail Home > Preferences; account card shows display name and email as read-only text (no inputs) and "Not available" with the rest of the page usable when the current-user call fails; four tabs Vehicle Types, Fuel Types, Transmissions, Drivetrains with the first open by default, `?tab=drivetrains` opens that tab, an unknown `tab` value opens the first; each tab lists every item including hidden ones with a "Default" or "Yours" badge, a "Hidden" badge on hidden items, and a Hide or Show button whose accessible name includes the item name (for example "Hide Sedan"); an add form with a labeled text input (`maxlength` 30) and an "Add" button; list uses list-group markup, not a table; a lookup load failure shows the shared `ApiFailureAlert`
- [X] T045 [P] [US3] `PreferencesFormsTests` in `tests/MyGarage.Web.Tests/Preferences/PreferencesFormsTests.cs` using `WebAppFactory` (static SSR posts): `add-lookup-{kind}` posts the name to the API and redirects to `/preferences?tab={slug}`; an API 400 or 409 re-renders the same tab with the message next to the add field, the other tabs unchanged, and the typed text kept; the 409 messages distinguish "already exists", "already exists but is hidden" (with a Show control), and "limit reached"; `toggle-lookup-{kind}-{itemId}` sends the inverse visibility and redirects to the same tab; a 404 shows a neutral "item not found" message on that tab; forms require an antiforgery token; an unauthenticated request redirects to sign-in and returns to `/preferences`
- [X] T046 [P] [US3] Extend `tests/MyGarage.Web.Tests/Common/UiStandardConformanceTests.cs` so it also scans the new `Preferences/*.razor` and `Common/TabNav.razor` sources (no `MarkupString`, no `<table>` for the lookup lists, no inline `style` or jQuery, icon-only controls have accessible names)

### Implementation for User Story 3

- [X] T047 [US3] Extract `src/MyGarage.Web/Common/TabNav.razor` (parameters: `Tabs` of `(string Id, string Label, IconName Icon)`, `Current`, optional `IdPrefix` defaulting to `tab`, producing the same markup as today's `VehicleTabNav`); change `src/MyGarage.Web/Vehicles/VehicleTabNav.razor` to render `TabNav` with `VehicleTabs.All`; T042 must pass and all existing vehicle tab tests keep passing
- [X] T048 [P] [US3] Create `src/MyGarage.Web/Preferences/PreferencesTabs.cs` (the four tabs: id = lookup slug from `LookupKinds`, label Vehicle Types / Fuel Types / Transmissions / Drivetrains, icons reusing existing `IconName` values, adding a new `IconName` entry and glyph in `src/MyGarage.Web/Common/Icon.razor` only if none fits; `Normalize(string?)` returning the first tab for unknown values)
- [X] T049 [P] [US3] Create `src/MyGarage.Web/Preferences/AccountDetails.razor`: a Bootstrap card "Account" with a read-only `dl` of Name (display name) and Email, text encoded, an explanatory line that account details come from the sign-in and cannot be changed here, "Not available" for a missing value
- [X] T050 [P] [US3] Create `src/MyGarage.Web/Preferences/LookupTab.razor` (parameters: kind slug, label, `IReadOnlyList<LookupItemDto>`, add-form errors, status message): a Bootstrap `list-group` with one row per item (name, badges "Default"/"Yours" and "Hidden", muted when hidden, a per-item `<form method="post" @formname="toggle-lookup-{kind}-{itemId}">` with antiforgery token and a button "Hide"/"Show" with an accessible name that includes the item name), then the add form (`@formname="add-lookup-{kind}"`, labeled input `maxlength="30"`, "Add" button, `ValidationMessage`-style error text linked with `aria-describedby`); render all text encoded
- [X] T051 [US3] Create `src/MyGarage.Web/Preferences/Preferences.razor`: `@page "/preferences"`, `[Authorize]`, `[ExcludeFromInteractiveRouting]`, `PageHeader Title="Preferences"` with trail Home > Preferences, `AccountDetails` (using `GarageApiClient.GetCurrentUserAsync`, swallowing failure into "Not available"), a card with `TabNav` over `PreferencesTabs` and a `tab-content` with one `LookupTab` pane per kind, `[SupplyParameterFromQuery(Name = "tab")]`; data from `LookupsApiClient.GetAsync(includeHidden: true)`; `OnValidSubmit`-style handlers for the add and toggle forms call `AddAsync`/`SetVisibilityAsync` and redirect (post-redirect-get) to `/preferences?tab={slug}`, re-rendering with errors for 400/409 and a neutral message for 404; T044 and T045 must pass
- [X] T052 [US3] Add the Preferences link to the account dropdown in `src/MyGarage.Web/Components/Layout/AppNavbar.razor` (`<li><a class="dropdown-item" href="/preferences">Preferences</a></li>` between "My vehicles" and the divider); T043 must pass

**Checkpoint**: US3 tests pass; quickstart steps 2.5-2.7 work in the browser.

---

## Phase 6: User Story 4 - Existing vehicles keep working (Priority: P2)

**Goal**: Vehicles saved before this feature still list and open with new details "Not set", prompt for the missing year, and require a year on their next edit.

**Independent Test**: With a pre-feature vehicle, view the garage and detail page, save an edit without a year (rejected), then add a year and other details (saved).

### Tests for User Story 4 (write first, confirm they fail)

- [X] T053 [P] [US4] Extend `tests/MyGarage.Database.Tests/Vehicles/VehicleRepositoryChangeTests.cs`: a vehicle inserted with `year` NULL (as migrated rows are) is returned by `ListPageAsync`, `GetByIdAsync`, and search/filter queries with `Year` null and every new field null; updating it with a year and new details persists them; Mileage, Color, and Doors stay unchanged by an update that does not alter them
- [X] T054 [P] [US4] Extend `tests/MyGarage.Api.Tests/Vehicles/VehicleChangeEndpointsTests.cs`: a vehicle created directly in the database without a year (insert through SQL helper) is returned by `GET /api/vehicles` and `GET /api/vehicles/{id}` with `year` null and no error; `PUT` without `year` -> 400 on `Year` and the stored vehicle is unchanged; `PUT` with a valid year -> 200 and the year is stored
- [X] T055 [P] [US4] Extend `tests/MyGarage.Web.Tests/Vehicles/VehicleDetailPageTests.cs` and `VehicleEditPageTests.cs`: a vehicle with no year shows a prompt ("Add the year") linking to `/vehicles/{id}/edit?tab=basic` on the detail page; the Specifications tab shows existing Mileage, Color, and Doors; saving an edit without a year shows the Year error on the Basic Info tab (the tab opens) and keeps the other entered values

### Implementation for User Story 4

- [X] T056 [US4] Update `src/MyGarage.Web/Vehicles/VehicleDetail.razor` to show a Bootstrap `alert alert-warning` prompt with the link in T055 when `vehicle.Year` is null (inside the Basic Info tab and also above the tabs so it is visible), and "Not set" for Year in the Basic Info list
- [X] T057 [US4] Confirm and, where needed, adjust `src/MyGarage.Web/Vehicles/VehicleEdit.razor` and `VehicleForm.razor` so a pre-feature vehicle's form loads with Year empty and required, null lookups select "Not set", and an API `Year` error opens the Basic Info tab; make T053-T055 pass (no data backfill, no migration change)

**Checkpoint**: US4 tests pass; quickstart steps 2.1-2.2 work against a pre-feature vehicle.

---

## Phase 7: User Story 5 - Year on cards and search/filter on the new details (Priority: P2)

**Goal**: Cards show the year; search also matches year, VIN, and engine; the garage gets a Filters panel with the existing filters and the new ones (Vehicle Type, Fuel Type, Transmission, Drivetrain, year range).

**Independent Test**: With vehicles differing in year, type, fuel, transmission, and drivetrain, confirm cards show the year, searching a year/VIN part/engine part returns matches, and each new filter narrows results together with existing filters.

### Tests for User Story 5 (write first, confirm they fail)

- [X] T058 [P] [US5] Extend `tests/MyGarage.Database.Tests/Vehicles/VehicleSearchTests.cs`: `q` matches `year` (as text), `vin`, and `engine` (case-insensitively, contains) in addition to the existing fields; `%` and `_` stay literal; `MinYear`/`MaxYear` inclusive and vehicles with null year never match a year bound; `VehicleTypeId`, `FuelTypeId`, `TransmissionId`, `DrivetrainId` match by id equality; all criteria combine with AND with the existing filters; `TotalCount` and paging reflect the filtered set; summary rows include `Year`; another owner's matching vehicles are never returned; `GetFilterOptionsAsync` additionally returns distinct `(id, name)` pairs for vehicle types, fuel types, transmissions, and drivetrains used by that owner's vehicles only (including items the owner has hidden), sorted by name
- [X] T059 [P] [US5] Extend `tests/MyGarage.Api.Tests/Vehicles/VehicleListQueryValidatorTests.cs`: `minYear` and `maxYear` 1900-2200 and `minYear <= maxYear`; the four lookup id filters accept only GUIDs (a non-GUID value is a 400); paging and existing rules unchanged
- [X] T060 [P] [US5] Extend `tests/MyGarage.Api.Tests/Vehicles/VehicleSearchEndpointsTests.cs`: `GET /api/vehicles` with each new criterion and combinations returns only matches with correct `totalCount`/`totalPages` and `year` on each summary; invalid criteria (`minYear` greater than `maxYear`, `fuelTypeId=abc`) -> 400 `ValidationProblem`; `GET /api/vehicles/filter-options` returns the new lists for the caller only (401 without token; user B sees none of user A's items)
- [X] T061 [P] [US5] Extend `tests/MyGarage.Web.Tests/Vehicles/VehicleCardTests.cs`: the card shows `year make model` (for example "2018 Toyota Tacoma") and omits the year when null; unchanged nickname link, mileage, picture, and placeholder
- [X] T062 [P] [US5] Update `tests/MyGarage.Web.Tests/Vehicles/VehicleSearchTests.cs` and add `VehicleFilterPanelTests.cs` in the same folder: `VehicleListCriteria` round-trips the new query keys (`minYear`, `maxYear`, `vehicleTypeId`, `fuelTypeId`, `transmissionId`, `drivetrainId`), ignores non-numeric years and non-GUID ids, lists each active criterion in `Active()` with a readable label (for example "Fuel type: Diesel" using the option names) and removes only that criterion with `Without()`; the Filters panel (collapsed unless a filter is active) renders selects for Make, Color, Doors, Tag, Vehicle Type, Fuel Type, Transmission, Drivetrain filled from the filter options, number inputs for Minimum/Maximum mileage and Minimum/Maximum year, labeled controls, "Apply" and "Clear all"; active criteria are shown as removable badges with accessible names; minimum year greater than maximum year shows the API's inline message and the page still renders; "No vehicles match" is distinct from the empty-garage state; pager links keep all criteria; the search box placeholder mentions year, VIN, and engine

### Implementation for User Story 5

- [X] T063 [US5] Update `src/MyGarage.Database/Vehicles/`: `VehicleSummaryRecord` gains `Year`; `VehicleSearchCriteria` gains `MinYear`, `MaxYear`, `VehicleTypeId`, `FuelTypeId`, `TransmissionId`, `DrivetrainId`; `VehicleFilterOptionsRecord` gains lists of `(Id, Name)` for the four lookups; update `Scripts/Vehicles/ListPage.sql` and `Count.sql` (select `v.year`; extend the search predicate with `v.year::text ILIKE @Pattern ESCAPE '\' OR v.vin ILIKE @Pattern ESCAPE '\' OR v.engine ILIKE @Pattern ESCAPE '\'`; add `@MinYear::int IS NULL OR v.year >= @MinYear`, `@MaxYear::int IS NULL OR v.year <= @MaxYear`, `@VehicleTypeId::uuid IS NULL OR v.vehicle_type_id = @VehicleTypeId`, and the same for fuel type, transmission, drivetrain; keep `v.owner_id = @OwnerId`) and `Scripts/Vehicles/FilterOptions.sql` (four more owner-scoped queries joining `lookup_items`, distinct by item id, ordered by `name_key`); pass the new parameters in `VehicleRepository.ListPageAsync` and read the new result sets in `GetFilterOptionsAsync`; T058 must pass
- [X] T064 [P] [US5] Update `src/MyGarage.Shared/Vehicles/VehicleSummaryDto.cs` (`Year?`) and `VehicleFilterOptionsDto.cs` (`VehicleTypes`, `FuelTypes`, `Transmissions`, `Drivetrains` as `LookupValueDto` lists); update `src/MyGarage.Api/Vehicles/VehicleMapper.cs` (`ToSummaryDto`, `ToCriteria`, filter options mapping)
- [X] T065 [US5] Update `src/MyGarage.Api/Vehicles/VehicleListQuery.cs`: add `MinYear`, `MaxYear`, `VehicleTypeId`, `FuelTypeId`, `TransmissionId`, `DrivetrainId` to the record and validator with the rules in T059 (`InclusiveBetween(1900, 2200)`, "Minimum year must not exceed maximum year."); pass them through `VehicleService.ListAsync` unchanged in shape; update the OpenAPI metadata descriptions of `GET /api/vehicles`; T059 and T060 must pass
- [X] T066 [US5] Update `src/MyGarage.Web/Vehicles/VehicleListCriteria.cs` with `MinYear`, `MaxYear`, `VehicleTypeId`, `FuelTypeId`, `TransmissionId`, `DrivetrainId` (parsing, `HasFilters`, `ToQueryString`, `Active()` labels, `Without()`; `FromQuery` takes the new values), and `src/MyGarage.Web/Vehicles/VehiclesApiClient.cs` so list requests send the new query keys; the badge labels for lookup filters take the item names from the filter options
- [X] T067 [P] [US5] Update `src/MyGarage.Web/Vehicles/VehicleCard.razor` to show `@Vehicle.Year @Vehicle.Make @Vehicle.Model` (no leading space when the year is null) and `src/MyGarage.Web/Vehicles/VehicleSearchCard.razor` placeholder text to "Search name, make, model, year, VIN, engine, trim, color, or tag"
- [X] T068 [US5] Create `src/MyGarage.Web/Vehicles/VehicleFilterPanel.razor`: a Bootstrap collapse (opened when `Criteria.HasFilters`) inside the existing GET search form containing selects for Make, Color, Doors (1-6), Tag, Vehicle Type, Fuel Type, Transmission, Drivetrain (choices from the filter options, first option "Any") and number inputs for Minimum/Maximum mileage and Minimum/Maximum year, an "Apply" button, "Clear all" link, and removable criteria badges (each a link to `Criteria.Without(key).ToHref()`) with accessible names; reuse the `q` value in a hidden input so search text is kept; no JavaScript beyond Bootstrap data attributes
- [X] T069 [US5] Update `src/MyGarage.Web/Vehicles/VehicleList.razor` to read all criteria from the query string with `[SupplyParameterFromQuery]` (as text), load `VehiclesApiClient.GetFilterOptionsAsync`, render `VehicleSearchCard` plus `VehicleFilterPanel`, keep the result summary in `aria-live="polite"`, show "No vehicles match" versus the empty-garage state, and keep invalid-criteria handling (show the messages, fall back to the whole garage); T061 and T062 must pass

**Checkpoint**: US5 tests pass; quickstart step 2.8 works.

---

## Phase 8: User Story 6 - Keep VIN and preferences private (Priority: P3)

**Goal**: Vehicle details, VIN, custom lookup items, and show/hide choices are visible and changeable only by their owner; VIN, vehicle field values, and custom item names never appear in logs or page addresses.

**Independent Test**: With two users, confirm cross-user requests are answered as not found for every new endpoint and id, and that logs and URLs contain no VIN or item names.

### Tests for User Story 6 (write first, confirm they fail)

- [X] T070 [P] [US6] Extend `tests/MyGarage.Api.Tests/Vehicles/OwnershipIsolationTests.cs` (table-driven): for every operation added or changed in `contracts/api.openapi.yaml` (`getLookups`, `addLookupItem`, `setLookupItemVisibility`, `getVehicle`, `updateVehicle`, `listVehicles` with the new filters, `getVehicleFilterOptions`), user B gets 404 or an empty result for user A's vehicle, custom lookup item, and hidden state, and nothing changed afterwards; user B cannot reference user A's custom item in a vehicle (400 with the same message as an unknown id)
- [X] T071 [P] [US6] Extend `tests/MyGarage.Api.Tests/Vehicles/LoggingTests.cs`: run create/update with a VIN, engine, and tire size, a search for the VIN, and adding/hiding a custom lookup item with a distinctive name through the API, and assert none of those values appear in captured log output (FR-022, research R13)
- [X] T072 [P] [US6] Add `tests/MyGarage.Web.Tests/Vehicles/VehiclePrivacyTests.cs` and `tests/MyGarage.Web.Tests/Preferences/PreferencesPrivacyTests.cs`: vehicle create/edit and lookup add/toggle submit through POST bodies with no VIN or item name in any generated link, form action, or redirect URL (the only query-string text is a user's own search `q`); the Preferences page and the vehicle forms require sign-in

### Implementation for User Story 6

- [X] T073 [US6] Fix any gap the tests in T070-T072 reveal (owner scoping in `src/MyGarage.Database/Scripts/Lookups/*.sql` and `Scripts/Vehicles/*.sql`, log statements in `src/MyGarage.Api/Lookups/` and `src/MyGarage.Api/Vehicles/`, redirects in `src/MyGarage.Web/Preferences/` and `src/MyGarage.Web/Vehicles/`); log only ids, kinds, counts, and outcomes

**Checkpoint**: US6 tests pass.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T074 [P] Update `docs/ui-standard.md`: the account menu lists the display name, My vehicles, Preferences, and Sign out; the vehicle view/edit pattern lists the new field placement on Basic Info and Specifications; add a "Preferences page" pattern (account card, four lookup tabs, Default/Yours/Hidden badges, Hide/Show buttons, add form) and the shared `TabNav` component; keep `UiStandardConformanceTests` consistent with the document
- [X] T075 [P] Update `specs/004-expanded-vehicle-details/contracts/web-routes.md` and `quickstart.md` only if implementation differs from them (for example final label or message text); update `src/README.md` and `tests/README.md` if they list features or test folders (`Lookups/`, `Preferences/`)
- [X] T076 [P] Accessibility and presentation pass on `src/MyGarage.Web/Vehicles/*.razor`, `src/MyGarage.Web/Preferences/*.razor`, and `src/MyGarage.Web/Common/TabNav.razor`: keyboard order and focus within tabs, labels and `aria-describedby` for validation messages, accessible names on Hide/Show and icon-only controls, `prefers-reduced-motion` respected, narrow-screen layout (tabs wrap or scroll within their container without page-level horizontal scroll); confirm no `MarkupString`, no jQuery, and no new custom JavaScript
- [X] T077 Run `dotnet build MyGarage.slnx` (zero warnings) and `dotnet test MyGarage.slnx` with Docker running; fix anything failing, including pre-existing tests affected by the stricter `Year` requirement (update their request builders to include a year)
- [X] T078 Run the full quickstart in `specs/004-expanded-vehicle-details/quickstart.md` (manual sections 2-3; sign-in steps use the integrated browser after asking the user to sign in); confirm migrations 0005 and 0006 apply once on the existing local database, existing vehicles survive, and fix any gaps found

---

## Phase 10: Amendment 1 (2026-11-02) - vehicle name, form layout, media, shared features

**Purpose**: The six follow-up changes recorded in `spec.md` (Amendment 1, FR-025 to FR-031).

- [X] T079 Add migration `0007_drop_vehicle_nickname.sql` (drop `nickname` and `ix_vehicles_owner_nickname`, add `ix_vehicles_owner_name`); remove `Nickname` from the vehicle SQL, records, mapper, validator, request, and DTOs; add `VehicleName.Compose` and a computed `Name` on `VehicleDetailDto` and `VehicleSummaryDto`; order the list by make, model, year, id and let search match "year make model"
- [X] T080 Add migration `0008_create_features.sql` (shared `features` catalog, `vehicle_tags` becomes the link table, backfill from existing tags); update the Tags SQL, `VehicleTagRepository.AddAsync` (find-or-create the feature) and add `SuggestAsync`; add `GET /api/features?query=` and the Web proxy `GET /features/suggest`
- [X] T081 Reorder `VehicleBasicFields.razor`, `VehicleSpecificationFields.razor`, `VehicleFormFields`, and the detail view to the new field order; hide Date Lease Ends unless Leased (server-rendered `hidden`, `lease-toggle.js`) and send no lease end for a vehicle that is not leased
- [X] T082 Rebuild the Media tab (`VehicleGallerySection.razor`): `mg-dropzone` upload form with `dropzone.js`, "Uploaded Images (N)" and square tiles, `CloudUpload` icon, styles in `app.css`
- [X] T083 Rebuild the Description tab: Features (`VehicleTagsSection.razor`, `VehicleTagsView.razor`) as a list group with remove buttons and a datalist-backed add box (`feature-suggest.js`), laid out beside the Journal in two `col-lg-6` columns on the edit and detail pages
- [X] T084 Remove "My vehicles" from the account menu in `AppNavbar.razor`; update the FAQ answer for adding a vehicle
- [X] T085 [P] Fix and extend `tests/MyGarage.Database.Tests` (migrations 0007 and 0008 including the backfill, shared-feature reuse, `SuggestAsync`, search by "year make model", new ordering)
- [X] T086 [P] Fix and extend `tests/MyGarage.Api.Tests` (no nickname, computed `Name`, ordering, feature sharing, `GET /api/features`)
- [X] T087 [P] Fix and extend `tests/MyGarage.Web.Tests` (new field order and groupings, lease end visibility, dropzone, features list group, half-width Description columns, account menu, `/features/suggest`, script conformance)
- [X] T088 [P] Update `docs/ui-standard.md`, `data-model.md`, and `contracts/web-routes.md`
- [X] T089 Run `dotnet build MyGarage.slnx` (zero warnings) and `dotnet test MyGarage.slnx` with Docker running
- [X] T090 Amendment 2: move the date and lease end onto the ownership row, move Mileage to Basic Info, make the date and mileage labels follow the ownership choice (`VehicleFormat.DateLabel` / `MileageLabel`, `lease-toggle.js`), restructure the Description tab (Add a feature then Included Features; Recent Journal Entries timeline), update tests and docs; spec `FR-032` to `FR-034`
- [X] T092 Show fuel type, transmission, and purchase/lease year on garage cards (list query, summary DTO, `VehicleCard.razor`, tests, docs); spec `FR-035`
- [X] T091 Manual pass in the browser (after signing in): add and edit a vehicle (field order, lease end toggling, no Name), upload by click and by drag and drop, add a feature that another vehicle already uses and a new one, remove a feature, confirm the account menu has no "My vehicles"; confirm migrations 0007 and 0008 apply once on the existing local database and the existing vehicle and its tags survive

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies; start immediately
- **Foundational (Phase 2)**: depends on Setup; BLOCKS all user stories (schema, lookup DTOs, read-only lookups API and client)
- **US1 (Phase 3)** depends on Foundational only; it is the MVP and works with the default lookup items alone
- **US2 (Phase 4)** depends on Foundational; its hidden-item rule (T040) edits the checker created in US1 (T024), so do T040 after US1
- **US3 (Phase 5)** depends on US2 (it calls add/hide/show) and on Foundational; independent of US1 except for the shared vehicle form check in the quickstart
- **US4 (Phase 6)** depends on US1 (year requirement and detail page)
- **US5 (Phase 7)** depends on US1 (columns and DTOs) and Foundational (lookup items for filters); its list changes share files with nothing in US2/US3
- **US6 (Phase 8)** depends on US1-US3 (verifies the finished endpoints and pages)
- **Polish (Phase 9)** depends on all stories being complete

### Within Each User Story

- Tests first and failing, then records/scripts, then repository, then Api validators/mappers/endpoints, then Web client and pages
- Tasks that edit the same file are not parallel with each other: `VehicleRequestValidator.cs` (T022), `VehicleMapper.cs` (T023, T064), `VehiclesEndpoints.cs` (T024), `VehicleLookupChecker.cs` (T024, T040), `LookupRepository.cs` (T009, T021, T037), `LookupService.cs` and `LookupsEndpoints.cs` (T010, T039), `VehicleListQuery.cs` (T065), `VehicleListCriteria.cs` (T066), `VehiclesApiClient.cs` (T066), `VehicleForm.razor` (T028, T057), `VehicleDetail.razor` (T030, T056), `VehicleList.razor` (T069), `AppNavbar.razor` (T052), `Program.cs` files (T010, T011)

### Story completion order

Foundational -> US1 -> (US2, US4, US5 in any order) -> US3 (after US2) -> US6 -> Polish

### Parallel Opportunities

- Foundational: T002-T005 together (test files); T006, T007, T008 together; T011 and T012 together after the DTOs exist
- US1: T013-T018 together (test files); T019, T021 together; T026 and T027 together after T025
- US2: T031-T035 together; T036, T038, T041 together
- US3: T042-T046 together; T048, T049, T050 together
- US4: T053-T055 together
- US5: T058-T062 together; T064 and T067 together
- US6: T070-T072 together
- Polish: T074-T076 together

### Parallel Example: User Story 1

```text
Together: T013, T014, T015, T016, T017, T018 (all test files)
Together: T019, T021 (different files)
Then: T020 -> T022 -> T023 -> T024 -> T025 -> (T026, T027) -> T028 -> T029 -> T030
```

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 and Phase 2
2. Complete Phase 3 (US1) and validate with quickstart steps 2.3-2.4
3. Demo: users can add and view vehicles with the new fields on the Basic Info and Specifications tabs using the default lookup choices

### Incremental Delivery

1. Add US2 and US3: users can hide, show, and add lookup items from the new Preferences page (the three P1 stories together deliver the requested lookup experience)
2. Add US4 (existing vehicles prompt for year) and US5 (year on cards, search and filters)
3. Add US6 verification of privacy
4. Finish with Polish (UI standard doc, accessibility, full test run, quickstart)

## Notes

- `[P]` tasks touch different files with no incomplete dependencies
- Each story's checkpoint is an independently demonstrable increment
- Quote and keep the validation limits exactly as written; if a limit must change, update `data-model.md` and `research.md` R6 first
- Never edit migrations `0001`-`0004`; add new numbered scripts only
- Commit after each task or logical group on branch `004-expanded-vehicle-details`; Conventional Commits
