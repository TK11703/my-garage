---

description: "Task list for Vehicle Service Records"
---

# Tasks: Vehicle Service Records

**Input**: Design documents from `/specs/005-service-records/`

**Prerequisites**: plan.md, spec.md, research.md (R1-R15), data-model.md, contracts/ (`api.openapi.yaml`, `web-routes.md`), quickstart.md

**Tests**: Included. The constitution (VIII) requires test-first development: write each story's tests, run them, and confirm they FAIL before implementing. Repository, migration, and API tests use real PostgreSQL via Testcontainers (Docker required); the database is never mocked.

**Organization**: Tasks are grouped by user story. Paths follow the structure in plan.md. Story numbers match spec.md: US1 add and view records (P1), US2 view/edit/delete a record (P1), US3 line items and total cost (P1), US4 privacy and cascade (P1), US5 garage mileage and Latest Odometer Reading (P2), US6 history summary (P3).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1-US6, only on user-story phase tasks
- Constraints from data-model.md and research.md are quoted in the task that creates the artifact; do not loosen them
- This feature adds no projects, packages, or Aspire resources (research R15)
- Every service-record SQL statement proves ownership with `JOIN vehicles v ON v.id = ... WHERE v.owner_id = @OwnerId` plus the route's `@VehicleId` (and `@RecordId` for line items); no service table has an `owner_id` column (research R2)

## Phase 1: Setup

**Purpose**: Confirm a clean baseline; there is no new project or package to add.

- [X] T001 On branch `005-service-records`, run `dotnet build MyGarage.slnx` and confirm zero warnings, then run `dotnet test tests/MyGarage.Web.Tests` (no Docker needed) to record the baseline; confirm `src/MyGarage.Database/Scripts/Migrations/` contains exactly `0001`-`0008` (never edit them)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The schema, the DTOs, the cost calculator, the persistence types, and the test helpers that every story needs.

**CRITICAL**: No user story work begins until this phase is complete.

### Tests first (confirm they fail)

- [X] T002 [P] Update `tests/MyGarage.Database.Tests/Common/MigrationRunnerTests.cs`: a fresh database now ends with scripts `0001`-`0010` applied and a ledger row for each; tables `service_records`, `service_line_items`, `service_line_item_parts` exist with the data-model.md columns; `vehicles.latest_odometer_reading` exists, is NULL on a vehicle that existed before `0010`, and every other vehicle column is unchanged; constraint checks: `service_records.odometer_reading` rejects -1 and 10000000, `date_serviced` rejects `1899-12-31`, `shop_or_person` rejects 101 characters; `service_line_items.flat_cost` rejects -0.01 and 10000000.00, `labor_hours` rejects 0 and 1000, `labor_rate` rejects -0.01 and 100000.00, `total_cost` rejects -0.01 and 10000000.00, `ck_service_line_items_labor_pair` rejects hours without a rate and a rate without hours, `ck_service_line_items_flat_or_breakdown` rejects a flat cost together with labor; `service_line_item_parts.quantity` rejects 0 and 10000, `unit_price` rejects -0.01, `(line_item_id, position)` is unique; `vehicles.latest_odometer_reading` rejects -1 and 10000000; deleting a vehicle cascades to its records, line items, and parts, deleting a record cascades to its line items and parts, and deleting a line item cascades to its parts; applied-script checksum drift still throws
- [X] T003 [P] `ServiceCostCalculatorTests` in `tests/MyGarage.Database.Tests/ServiceRecords/ServiceCostCalculatorTests.cs`: flat cost returns exactly the flat cost (including 0.00); labor `round(hours x rate, 2)` with `MidpointRounding.AwayFromZero` (1.5 h x 90.00 = 135.00; 0.33 h x 10.00 = 3.30; 0.07 h x 12.50 = 0.88 from 0.875); each part is `round(quantity x unit price, 2)` and parts are summed after rounding (2.5 x 3.33 = 8.33 from 8.325); labor plus several parts adds all of them (1.5 h x 90.00 + 1 x 12.50 + 5 x 8.25 = 188.75); a breakdown with no labor and parts only works; a breakdown with labor and no parts works; results keep two decimal places; no overflow at the maximum 9,999,999.99 flat cost

### Implementation

- [X] T004 [P] Create `src/MyGarage.Database/Scripts/Migrations/0009_create_service_records.sql` per data-model.md. `service_records`: `id` uuid PRIMARY KEY; `vehicle_id` uuid NOT NULL REFERENCES `vehicles (id)` ON DELETE CASCADE; `date_serviced` date NOT NULL CHECK (`date_serviced >= '1900-01-01'`); `odometer_reading` integer NOT NULL CHECK (`odometer_reading BETWEEN 0 AND 9999999`); `shop_or_person` varchar(100) NULL; `created_at`, `updated_at` timestamptz NOT NULL; `created_by`, `updated_by` text NOT NULL; index `ix_service_records_vehicle_serviced (vehicle_id, date_serviced DESC, created_at DESC, id DESC)`. `service_line_items`: `id` uuid PRIMARY KEY; `service_record_id` uuid NOT NULL REFERENCES `service_records (id)` ON DELETE CASCADE; `description` varchar(200) NOT NULL; `flat_cost` numeric(12,2) NULL CHECK (`BETWEEN 0 AND 9999999.99`); `labor_hours` numeric(5,2) NULL CHECK (`BETWEEN 0.01 AND 999.99`); `labor_rate` numeric(7,2) NULL CHECK (`BETWEEN 0 AND 99999.99`); `total_cost` numeric(12,2) NOT NULL CHECK (`BETWEEN 0 AND 9999999.99`); `created_at`, `updated_at` timestamptz NOT NULL; `created_by`, `updated_by` text NOT NULL; `CONSTRAINT ck_service_line_items_labor_pair CHECK ((labor_hours IS NULL) = (labor_rate IS NULL))`; `CONSTRAINT ck_service_line_items_flat_or_breakdown CHECK (flat_cost IS NULL OR labor_hours IS NULL)`; index `ix_service_line_items_record (service_record_id, created_at, id)`. `service_line_item_parts`: `id` uuid PRIMARY KEY; `line_item_id` uuid NOT NULL REFERENCES `service_line_items (id)` ON DELETE CASCADE; `position` smallint NOT NULL; `name` varchar(100) NOT NULL; `quantity` numeric(6,2) NOT NULL CHECK (`BETWEEN 0.01 AND 9999.99`); `unit_price` numeric(12,2) NOT NULL CHECK (`BETWEEN 0 AND 9999999.99`); `UNIQUE (line_item_id, position)`
- [X] T005 [P] Create `src/MyGarage.Database/Scripts/Migrations/0010_add_vehicle_latest_odometer.sql`: `ALTER TABLE vehicles ADD COLUMN latest_odometer_reading integer NULL CHECK (latest_odometer_reading BETWEEN 0 AND 9999999);` and do not touch the existing `mileage` column
- [X] T006 [P] Create the DTOs in `src/MyGarage.Shared/ServiceRecords/` (one file each, DTOs only, no owner or user id fields, matching `contracts/api.openapi.yaml`): `ServiceRecordRequest` (`DateServiced` `DateOnly?`, `OdometerReading` `int?`, `ShopOrPerson` `string?`), `ServiceRecordSummaryDto` (`Id`, `DateServiced` `DateOnly`, `OdometerReading` int, `ShopOrPerson?`, `LineItemCount` int, `TotalCost` decimal), `ServiceRecordDto` (`Id`, `VehicleId`, `DateServiced`, `OdometerReading`, `ShopOrPerson?`, `LineItems` `IReadOnlyList<ServiceLineItemDto>`, `TotalCost`, `CreatedAt`, `UpdatedAt` `DateTimeOffset`), `ServiceLineItemRequest` (`Description` `string?`, `FlatCost` `decimal?`, `LaborHours` `decimal?`, `LaborRate` `decimal?`, `Parts` `IReadOnlyList<ServicePartRequest>` defaulting to empty), `ServicePartRequest` (`Name` `string?`, `Quantity` `decimal?`, `UnitPrice` `decimal?`), `ServiceLineItemDto` (`Id`, `Description`, `FlatCost?`, `LaborHours?`, `LaborRate?`, `LaborCost?`, `Parts` `IReadOnlyList<ServicePartDto>`, `TotalCost`), `ServicePartDto` (`Name`, `Quantity`, `UnitPrice`, `TotalPrice`); request fields are nullable so a missing value is reported by validation rather than defaulting to 0
- [X] T007 [P] Create `src/MyGarage.Database/ServiceRecords/ServiceCostCalculator.cs` (public static, pure): `LaborCost(decimal hours, decimal rate)`, `PartTotal(decimal quantity, decimal unitPrice)`, and `LineTotal(decimal? flatCost, decimal? laborHours, decimal? laborRate, IEnumerable<(decimal Quantity, decimal UnitPrice)> parts)` per research R6: flat cost when present, otherwise `LaborCost` (when labor is present) plus the sum of `PartTotal`; every product uses `decimal.Round(value, 2, MidpointRounding.AwayFromZero)`; T003 must pass
- [X] T008 [P] Create the persistence types in `src/MyGarage.Database/ServiceRecords/` (classes or records with `init` properties and a parameterless constructor so Dapper maps them; internal to the Database project's public surface only as needed by Api mapping): `ServiceRecordRecord` (`Id`, `VehicleId`, `DateServiced` `DateOnly`, `OdometerReading`, `ShopOrPerson?`, `LineItemCount`, `TotalCost`, `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy`), `ServiceLineItemRecord` (`Id`, `Description`, `FlatCost?`, `LaborHours?`, `LaborRate?`, `TotalCost`, `Parts`), `ServiceLinePartRecord` (`Position`, `Name`, `Quantity`, `UnitPrice`), `ServiceRecordDetailRecord` (a record plus `LineItems`), and `ServiceLineItemInput` / `ServicePartInput` (the values the repository writes); register a Dapper `DateOnly` type handler in `src/MyGarage.Database/Common/DapperSetup.cs` only if it is not already registered there
- [X] T009 [P] Add test helpers: in `tests/MyGarage.Database.Tests/Common/TestData.cs` (or a new `tests/MyGarage.Database.Tests/ServiceRecords/ServiceRecordTestData.cs`) helpers that insert a vehicle for an owner and insert a service record, line item, and parts directly with SQL for a given vehicle; in `tests/MyGarage.Api.Tests/ServiceRecords/ServiceRecordTestData.cs` helpers to create a vehicle and then call the service record endpoints for a given test user; `ApiFactory.ResetDataAsync()` in `tests/MyGarage.Api.Tests/Common/ApiFactory.cs` needs no change because `TRUNCATE TABLE vehicles CASCADE` already clears the new tables, but confirm that with a test in T011
- [X] T010 [P] Add `Wrench` to `src/MyGarage.Web/Common/IconName.cs` and its glyph to `src/MyGarage.Web/Common/Icon.razor` (follow how the existing icons are drawn); extend `tests/MyGarage.Web.Tests/Common/IconTests.cs` to render it with an accessible name when one is supplied

**Checkpoint**: migrations 0001-0010 apply to an empty database; T002 and T003 pass; all pre-existing tests still pass.

---

## Phase 3: User Story 1 - Add and view service records for a vehicle (Priority: P1) MVP

**Goal**: A signed-in user opens a vehicle's Service Records tab, adds a record (Date Serviced, Odometer Reading, Shop Name or Person), and sees it in the list, newest service first, with paging and an empty state.

**Independent Test**: Open a vehicle with no records and confirm the empty state; add a record and confirm it appears with its values; add a second record with an earlier date and confirm the newer service stays first; confirm a second vehicle does not show them.

### Tests for User Story 1 (write first, confirm they fail)

- [X] T011 [P] [US1] `ServiceRecordRepositoryTests` in `tests/MyGarage.Database.Tests/ServiceRecords/ServiceRecordRepositoryTests.cs` for `AddAsync`, `GetAsync`, `ListPageAsync`: add returns the record with `LineItemCount` 0 and `TotalCost` 0.00 and `created_by`/`updated_by` equal to the owner id; an unknown vehicle or another owner's vehicle returns null and inserts nothing; list orders by `date_serviced DESC, created_at DESC, id DESC` (two records on the same date: the later-created first), paged with correct `TotalCount` and `TotalPages`, a page past the last is empty; records of vehicle A never appear for vehicle B; the list returns null for a vehicle that is not the owner's; `GetAsync` returns the record only through its own vehicle and owner; a record with `shop_or_person` NULL round-trips as null; after `ApiFactory`-style reset the new tables are empty (guards T009)
- [X] T012 [P] [US1] `ServiceRecordRequestValidatorTests` in `tests/MyGarage.Api.Tests/ServiceRecords/ServiceRecordRequestValidatorTests.cs` (fixed clock): `dateServiced` required, not after today's UTC date, not before 1900-01-01 (boundaries 1899-12-31, 1900-01-01, today, tomorrow); `odometerReading` required, whole number 0-9,999,999 (boundaries -1, 0, 9999999, 10000000); `shopOrPerson` optional, trimmed, at most 100 characters (boundaries 100, 101), whitespace-only accepted as "none", control characters rejected; messages are specific and keyed by property name (`DateServiced`, `OdometerReading`, `ShopOrPerson`)
- [X] T013 [P] [US1] `ServiceRecordMapperTests` in `tests/MyGarage.Api.Tests/ServiceRecords/ServiceRecordMapperTests.cs`: request -> repository values (shop trimmed, blank -> null); `ServiceRecordRecord` -> `ServiceRecordSummaryDto`; `ServiceRecordDetailRecord` -> `ServiceRecordDto` (empty `LineItems` list and `TotalCost` 0.00 for a new record); `PagedResult<ServiceRecordRecord>` -> `PagedResultDto<ServiceRecordSummaryDto>`; no DTO exposes `created_by`, an owner, or a user id
- [X] T014 [P] [US1] `ServiceRecordsEndpointsTests` (list, create, get) in `tests/MyGarage.Api.Tests/ServiceRecords/ServiceRecordsEndpointsTests.cs` using `ApiFactory`: `GET /api/vehicles/{vehicleId}/service-records` default page size 10 newest `dateServiced` first, an out-of-range `page` is clamped to the last page (the returned `page` is the page served), `pageSize=51` and `page=0` -> 400 `ValidationProblem`; `POST` -> 201 with a `Location` header and a `ServiceRecordDto` with no line items and total 0.00; blank `shopOrPerson` is stored as null; 400 `ValidationProblem` keyed by `dateServiced`/`odometerReading`/`shopOrPerson` for each invalid case in T012; `GET .../{recordId}` -> 200 with the record; unknown or other-vehicle ids -> 404 `application/problem+json`; 401 without a token for each endpoint; a body carrying `ownerId` or `userId` is ignored
- [X] T015 [P] [US1] Web tests: `tests/MyGarage.Web.Tests/ServiceRecords/ServiceRecordsViewTests.cs` (the tab content): empty state "No service records yet" with an "Add service record" link to `/vehicles/{id}/service-records/new`; one row per record (most recent first) showing the date, odometer formatted like "45,000 mi", shop or person ("Not set" when empty), "N line items" (singular "1 line item"), and the total cost formatted like "$1,234.50", each row linking to the record page; `Pager` links keep `tab=service-records` and use `recordsPage` (page 1 has no parameter); an API failure shows `ApiFailureAlert` and the rest of the page stays usable; `ServiceRecordCreatePageTests.cs`: the form has labeled Date serviced (date), Odometer reading (number, `inputmode="numeric"`, "mi"), and Shop name or person (text, `maxlength="100"`) inputs, required fields marked with `aria-required`, API 400 errors appear next to their field with `aria-describedby` and the typed values are kept, success redirects to the new record page, a 404 vehicle shows the not-found state; `ServiceRecordDetailPageTests.cs`: metadata shown as a definition list with "Not set" for an empty shop, a "Total cost" of `$0.00` and the empty line item state, a 404 shows the not-found state with a link back to `/vehicles/{id}?tab=service-records`; extend `tests/MyGarage.Web.Tests/Vehicles/VehicleDetailPageTests.cs`: a fourth tab "Service Records" appears after Description, `?tab=service-records` opens it, an unknown tab still opens Details

### Implementation for User Story 1

- [X] T016 [US1] Create the SQL in `src/MyGarage.Database/Scripts/ServiceRecords/` (parameterized, no string building): `VehicleExists.sql` (`SELECT EXISTS (SELECT 1 FROM vehicles WHERE id = @VehicleId AND owner_id = @OwnerId)`); `Insert.sql` (`INSERT ... SELECT @Id, v.id, @DateServiced, @OdometerReading, @ShopOrPerson, @Now, @Now, @OwnerId, @OwnerId FROM vehicles v WHERE v.id = @VehicleId AND v.owner_id = @OwnerId RETURNING ...`); `GetById.sql` (the record joined to `vehicles` on id and owner, with `LineItemCount` and `TotalCost` as `COUNT(li.id)` and `COALESCE(SUM(li.total_cost), 0)` from a left join of `service_line_items`, filtered by `@RecordId`, `@VehicleId`, `@OwnerId`); `ListPage.sql` (same projection, `WHERE sr.vehicle_id = @VehicleId` and owner join, `ORDER BY sr.date_serviced DESC, sr.created_at DESC, sr.id DESC LIMIT @PageSize OFFSET @Offset`); `Count.sql`; add the `.sql` files as embedded resources only if the existing script store requires it (follow how `Scripts/JournalNotes/*.sql` are included in `MyGarage.Database.csproj`)
- [X] T017 [US1] Implement `src/MyGarage.Database/ServiceRecords/ServiceRecordRepository.cs` (primary constructor `NpgsqlDataSource dataSource, SqlScriptStore scripts`, every method takes a `CancellationToken`): `AddAsync(ownerId, vehicleId, recordId, dateServiced, odometerReading, shopOrPerson, now, ct)` -> `ServiceRecordRecord?`; `ListPageAsync(ownerId, vehicleId, page, pageSize, ct)` -> `PagedResult<ServiceRecordRecord>?` (null when the vehicle is not the owner's, like `JournalNoteRepository.ListPageAsync`); `GetAsync(ownerId, vehicleId, recordId, ct)` -> `ServiceRecordDetailRecord?` with an empty line item list for now; register the repository in `src/MyGarage.Database/Common/DatabaseServiceCollectionExtensions.cs`; T011 must pass
- [X] T018 [P] [US1] Implement `src/MyGarage.Api/ServiceRecords/ServiceRecordRequestValidator.cs` (constructor takes `TimeProvider`, `Today()` = UTC date like `VehicleRequestValidator`) with exactly the rules in T012, using `TrimmedText("Shop name or person", 100, required: false)` from `src/MyGarage.Api/Common/TextRules.cs`; messages such as "Date serviced is required.", "Date serviced cannot be in the future.", "Odometer reading must be between 0 and 9,999,999."; T012 must pass
- [X] T019 [US1] Implement `src/MyGarage.Api/ServiceRecords/ServiceRecordMapper.cs` (explicit mapping per T013) and `src/MyGarage.Api/ServiceRecords/ServiceRecordsEndpoints.cs`: `AddServiceRecords()` registers the validators (`IValidator<ServiceRecordRequest>` plus the existing `PagingQuery` validator if not already registered) and `MapServiceRecordsEndpoints()` maps `MapGroup("/api/vehicles/{vehicleId:guid}/service-records").WithTags("ServiceRecords")` with `GET ""` (`ListServiceRecords`, default page size 10, clamping like `JournalNotesEndpoints`), `POST ""` (`CreateServiceRecord`, `Guid.CreateVersion7()`, 201 with `Location` `/api/vehicles/{vehicleId}/service-records/{id}`), and `GET "/{recordId:guid}"` (`GetServiceRecord`); owner from `CurrentOwner.Get(user)` with `VehiclesEndpoints.NoOwner()`/`NotFound()` helpers exactly as `JournalNotesEndpoints`; `AddEndpointFilter<ValidationFilter<...>>`; declare `Produces`/`ProducesProblem` for 200/201/400/401/403/404; call `services.AddServiceRecords()` and `app.MapServiceRecordsEndpoints()` in `src/MyGarage.Api/Program.cs`; T013 and T014 must pass; log ids and outcomes only
- [X] T020 [P] [US1] Create `src/MyGarage.Web/ServiceRecords/ServiceRecordsApiClient.cs` (typed `HttpClient` using the internal `ApiCalls.SendAsync` from `src/MyGarage.Web/Vehicles/ApiCalls.cs`): `ListAsync(Guid vehicleId, int page, ct)` -> `ApiResult<PagedResultDto<ServiceRecordSummaryDto>>`, `CreateAsync(Guid vehicleId, ServiceRecordRequest, ct)` -> `ApiResult<ServiceRecordDto>`, `GetAsync(Guid vehicleId, Guid recordId, ct)` -> `ApiResult<ServiceRecordDto>`; register `AddHttpClient<ServiceRecordsApiClient>(...)` in `src/MyGarage.Web/Program.cs` with the same base address and token handler as `VehiclesApiClient`; add `src/MyGarage.Web/ServiceRecords/ServiceRecordFormat.cs` (invariant-culture helpers: `Money(decimal)` -> `$1,234.50`, `Odometer(int)` -> `45,000 mi`, `Date(DateOnly)` in the same readable form `VehicleFormat.Date` uses, `LineItems(int)` -> "1 line item"/"N line items", `ShopOrNotSet(string?)`)
- [X] T021 [US1] Create the add page in `src/MyGarage.Web/ServiceRecords/`: `ServiceRecordFormModel.cs` (`DateServiced` `DateOnly?`, `OdometerReading` `int?`, `ShopOrPerson` `string?`, `ToRequest()`, `From(ServiceRecordDto)`), `ServiceRecordForm.razor` (`EditForm` with `FormName`, labeled `InputDate`, `InputNumber<int?>` with `min="0" max="9999999" step="1" inputmode="numeric"`, and `InputText maxlength="100"`, `aria-required` on required fields, `aria-describedby` + validation message per field, user text encoded), and `ServiceRecordCreate.razor` (`@page "/vehicles/{VehicleId:guid}/service-records/new"`, `[Authorize]`, `[ExcludeFromInteractiveRouting]`, `[SupplyParameterFromForm(FormName = "create-service-record")]`, `PageHeader` trail Vehicles > vehicle name (load the vehicle through `VehiclesApiClient.GetAsync`) > Add service record, post-redirect-get to `/vehicles/{id}/service-records/{recordId}`, API 400 errors re-render beside fields, 404 shows `EmptyState` with a 404 status like `VehicleDetail.razor`, `ApiFailureAlert` for failures); T015 create tests must pass
- [X] T022 [US1] Add the tab: in `src/MyGarage.Web/Vehicles/VehicleTabs.cs` append `("service-records", "Service Records", IconName.Wrench)`; create `src/MyGarage.Web/ServiceRecords/ServiceRecordsView.razor` (parameters `VehicleId`, `RecordsPage`; loads `ServiceRecordsApiClient.ListAsync`; `EmptyState` with the "Add service record" action when `TotalCount` is 0; a Bootstrap list-group (not a table) with one linked row per record using `ServiceRecordFormat`; `Pager` with `HrefFor` building `/vehicles/{id}?tab=service-records` and `&recordsPage={n}` for pages above 1; `ApiFailureAlert` on failure) and `ServiceRecordRow.razor` if the row markup is non-trivial; in `src/MyGarage.Web/Vehicles/VehicleDetail.razor` add the `tab-service-records` pane (`role="tabpanel"`, `aria-labelledby="tab-service-records-tab"`, same card wrapper as the other panes) and a `[SupplyParameterFromQuery(Name = "recordsPage")] string? RecordsPage` read as text (bad value -> page 1) like `NotesPage`; update the comment on `Tab` to list the new tab; T015 tab tests must pass
- [X] T023 [US1] Create `src/MyGarage.Web/ServiceRecords/ServiceRecordDetail.razor` (`@page "/vehicles/{VehicleId:guid}/service-records/{RecordId:guid}"`, `[Authorize]`, `[ExcludeFromInteractiveRouting]`): `PageHeader` trail Vehicles > vehicle name > "Service record {date}" with a link back to `/vehicles/{id}?tab=service-records`; a `dl` with Date serviced, Odometer reading, Shop name or person ("Not set"); a "Line items" section showing "No line items yet. Add what was done and what it cost." (the add link and list arrive in US3); a prominent "Total cost" figure (`$0.00` when none); 404 sets the response status and shows `EmptyState` with a link back to the Service Records tab; T015 detail tests must pass

**Checkpoint**: US1 tests pass; quickstart steps 1, 2, and 6 (ordering only) work.

---

## Phase 4: User Story 2 - View, edit, and delete a service record (Priority: P1)

**Goal**: A user corrects a record's metadata and deletes a record entered in error, with a confirmation first.

**Independent Test**: Add a record, edit a value and see it saved and the list order follow the new date, then delete it after cancelling once and confirm it is gone.

### Tests for User Story 2 (write first, confirm they fail)

- [X] T024 [P] [US2] Extend `tests/MyGarage.Database.Tests/ServiceRecords/ServiceRecordRepositoryTests.cs`: `UpdateAsync` changes date, odometer, and shop, sets `updated_at`/`updated_by` but not `created_at`/`created_by`, leaves line items untouched, and returns null for an unknown record, a record reached through a different vehicle of the same owner, and another owner's record (nothing changes); `DeleteAsync` removes only that record and its line items and parts and returns false in the same three cases; a changed `date_serviced` changes the record's place in `ListPageAsync`
- [X] T025 [P] [US2] Extend `tests/MyGarage.Api.Tests/ServiceRecords/ServiceRecordsEndpointsTests.cs`: `PUT /api/vehicles/{vehicleId}/service-records/{recordId}` -> 200 with the updated record (line items unchanged), 400 `ValidationProblem` for the T012 cases, 404 for unknown ids and the wrong vehicle; `DELETE` -> 204 then `GET` -> 404, 404 for unknown ids and the wrong vehicle; 401 without a token for both
- [X] T026 [P] [US2] Web tests: `tests/MyGarage.Web.Tests/ServiceRecords/ServiceRecordEditPageTests.cs`: `/vehicles/{id}/service-records/{recordId}/edit` pre-fills the three fields, a rejected save re-renders with messages beside the fields and the typed values kept, success redirects to the record page, 404 shows the not-found state; extend `ServiceRecordDetailPageTests.cs`: Edit link and a Delete control using the existing `<details>` confirmation panel (text "Delete this service record? This also removes its line items and cannot be undone.", form `delete-service-record`, buttons "Yes, delete this service record" and "Keep service record"), confirming calls the API and redirects to `/vehicles/{id}?tab=service-records`, cancelling leaves the record

### Implementation for User Story 2

- [X] T027 [US2] Add `src/MyGarage.Database/Scripts/ServiceRecords/Update.sql` (`UPDATE service_records sr SET date_serviced = @DateServiced, odometer_reading = @OdometerReading, shop_or_person = @ShopOrPerson, updated_at = @Now, updated_by = @OwnerId FROM vehicles v WHERE sr.id = @RecordId AND sr.vehicle_id = @VehicleId AND v.id = sr.vehicle_id AND v.owner_id = @OwnerId RETURNING sr.id`) and `Delete.sql` (`DELETE FROM service_records sr USING vehicles v WHERE sr.id = @RecordId AND sr.vehicle_id = @VehicleId AND v.id = sr.vehicle_id AND v.owner_id = @OwnerId`), mirroring `Scripts/JournalNotes/Update.sql` and `Delete.sql`
- [X] T028 [US2] Add `UpdateAsync(ownerId, vehicleId, recordId, dateServiced, odometerReading, shopOrPerson, now, ct)` -> `ServiceRecordDetailRecord?` and `DeleteAsync(ownerId, vehicleId, recordId, ct)` -> `bool` to `src/MyGarage.Database/ServiceRecords/ServiceRecordRepository.cs`; T024 must pass
- [X] T029 [US2] Add `PUT "/{recordId:guid}"` (`UpdateServiceRecord`, `ValidationFilter<ServiceRecordRequest>`) and `DELETE "/{recordId:guid}"` (`DeleteServiceRecord`, 204) to `src/MyGarage.Api/ServiceRecords/ServiceRecordsEndpoints.cs` with the same `Produces`/`ProducesProblem` metadata as the contract; T025 must pass
- [X] T030 [P] [US2] Add `UpdateAsync(Guid vehicleId, Guid recordId, ServiceRecordRequest, ct)` and `DeleteAsync(Guid vehicleId, Guid recordId, ct)` to `src/MyGarage.Web/ServiceRecords/ServiceRecordsApiClient.cs`
- [X] T031 [US2] Create `src/MyGarage.Web/ServiceRecords/ServiceRecordEdit.razor` (`@page "/vehicles/{VehicleId:guid}/service-records/{RecordId:guid}/edit"`, form name `update-service-record`, reuse `ServiceRecordForm.razor` and `ServiceRecordFormModel.From`), and add the Edit link and the Delete confirmation panel to `src/MyGarage.Web/ServiceRecords/ServiceRecordDetail.razor` following the delete panel in `src/MyGarage.Web/Vehicles/VehicleDetail.razor` (`mg-confirm-panel`, antiforgery token, `@formname="delete-service-record"`, a 404 on delete counts as success); T026 must pass

**Checkpoint**: US2 tests pass; quickstart step 7 (records part) works.

---

## Phase 5: User Story 3 - Itemize the work and see the total cost (Priority: P1)

**Goal**: A record carries line items, each priced as a flat cost or as labor (hours at a rate) plus parts, with each line's cost and the record total shown, and line items can be edited and deleted.

**Independent Test**: On a record, add a flat-cost line and a labor-and-parts line, confirm line costs and the total, edit one line's pricing and delete another, and confirm the total follows.

### Tests for User Story 3 (write first, confirm they fail)

- [X] T032 [P] [US3] Line item repository tests in `tests/MyGarage.Database.Tests/ServiceRecords/ServiceLineItemRepositoryTests.cs`: `AddLineItemAsync` stores a flat-cost line with `total_cost` equal to the flat cost and a labor-and-parts line with `total_cost` equal to `ServiceCostCalculator.LineTotal`, parts stored with `position` 1..n in the submitted order; `GetAsync` returns line items in the order they were added (`created_at, id`) with their parts in position order and a `TotalCost` equal to the sum of line totals; `ListPageAsync` shows `LineItemCount` and `TotalCost` per record without loading parts; `UpdateLineItemAsync` replaces description, flat cost, labor, and the whole parts set in one transaction (switching flat -> breakdown clears `flat_cost`, breakdown -> flat removes labor and parts), recomputes `total_cost`, and sets `updated_at`/`updated_by`; `DeleteLineItemAsync` removes the line and its parts and the record total follows; the 51st line item on one record returns `LimitReached` while another record is unaffected, and 20 concurrent adds to one record never exceed 50 (row lock, research R11); a failing insert part-way (for example a part violating a check constraint) rolls the whole write back and leaves the old line item intact; null/false results for an unknown record, an unknown line item, a line item reached through another record of the same vehicle, a record reached through another vehicle, and another owner's vehicle
- [X] T033 [P] [US3] `ServiceLineItemRequestValidatorTests` and `ServicePartRequestValidatorTests` in `tests/MyGarage.Api.Tests/ServiceRecords/`: `description` required, trimmed, 1-200 characters, no control characters (boundaries 0, 1, 200, 201, spaces only); `flatCost` 0.00-9,999,999.99 with at most two decimals (-0.01, 0, 9999999.99, 10000000, 1.005 rejected); `laborHours` 0.01-999.99 at most two decimals (0, 0.01, 999.99, 1000, 1.005); `laborRate` 0.00-99,999.99 at most two decimals; `laborHours` and `laborRate` required together (one without the other is an error on the missing one); exactly one pricing mode: flat cost with labor or with any part is rejected ("Use either a flat cost or labor and parts, not both."), no flat cost with neither labor nor parts is rejected ("Enter a flat cost, or labor hours and a rate, or at least one part."), flat cost 0.00 alone is accepted; `parts` at most 25 (25 accepted, 26 rejected); each part: `name` required trimmed 1-100 characters, `quantity` 0.01-9,999.99 at most two decimals (0, 0.01, 9999.99, 10000, 1.005), `unitPrice` 0.00-9,999,999.99 at most two decimals; the computed line total (via `ServiceCostCalculator.LineTotal`) above 9,999,999.99 is rejected with a message on the request; errors are keyed `Description`, `FlatCost`, `LaborHours`, `LaborRate`, `Parts[0].Name`, `Parts[0].Quantity`, `Parts[0].UnitPrice`
- [X] T034 [P] [US3] Extend `tests/MyGarage.Api.Tests/ServiceRecords/ServiceRecordMapperTests.cs`: `ServiceLineItemRecord` -> `ServiceLineItemDto` (flat line has `LaborCost` null and no parts; breakdown line has `LaborCost` = `ServiceCostCalculator.LaborCost`, each `ServicePartDto.TotalPrice` = `PartTotal`, `TotalCost` from the record); `ServiceLineItemRequest` -> `ServiceLineItemInput` (description trimmed, blank part rows are not produced by the API, part order kept)
- [X] T035 [P] [US3] Line item endpoint tests in `tests/MyGarage.Api.Tests/ServiceRecords/ServiceLineItemsEndpointsTests.cs`: `POST .../line-items` -> 201 returning the whole `ServiceRecordDto` with the new line and updated `totalCost` (flat 40.00 -> 40.00; 1.5 h x 90.00 with parts 1 x 12.50 and 5 x 8.25 -> 188.75; both -> 228.75); 400 `ValidationProblem` for each invalid case in T033 including a flat cost with labor; 409 `ProblemDetails` on the 51st line item; 404 for unknown record and wrong vehicle; `PUT .../line-items/{lineItemId}` -> 200 (flat -> breakdown and back, total follows), 400, 404 (unknown line item, line item of another record); `DELETE` -> 204 and the record total follows, 404 otherwise; 401 for each without a token; a request carrying a part `id` or an owner id is ignored
- [X] T036 [P] [US3] Web tests: extend `tests/MyGarage.Web.Tests/ServiceRecords/ServiceRecordDetailPageTests.cs`: line items listed in order with the description, a pricing summary ("Flat cost $40.00"; "Labor: 1.5 h x $90.00 = $135.00" plus "2 x Oil filter at $12.50 = $25.00" rows) and the line total, Edit and Delete per line (row-level `btn-sm`, accessible names include the description), the delete panel (form `delete-line-item-{lineItemId}`) redirects back to the record page, an "Add line item" link, the "Total cost" equals the API's `totalCost`, the empty state text when none; `tests/MyGarage.Web.Tests/ServiceRecords/ServiceLineItemFormTests.cs`: "What was done" input (`maxlength="200"`), a "Flat cost" / "Labor and parts" radio pair defaulting to Flat cost on add and to the saved mode on edit, flat cost input (`step="0.01"`), labor hours and hourly rate inputs, parts rows (Part name `maxlength="100"`, Quantity, Price per part) showing the existing parts plus three blank rows, "Add more part rows" (form `add-part-rows`) renders three more without saving up to 25, all-blank part rows are ignored on save, a partly filled row is submitted and its API error is shown on that row ("Part 2: Quantity must be between 0.01 and 9,999.99."), the pricing mode that is not selected is discarded on save, API 400 messages appear beside their fields, a 409 shows "A service record can have up to 50 line items.", a 404 shows the not-found state, success redirects to the record page

### Implementation for User Story 3

- [X] T037 [US3] Create the SQL in `src/MyGarage.Database/Scripts/ServiceRecords/`: `LockRecord.sql` (`SELECT sr.id FROM service_records sr JOIN vehicles v ON v.id = sr.vehicle_id WHERE sr.id = @RecordId AND sr.vehicle_id = @VehicleId AND v.owner_id = @OwnerId FOR UPDATE OF sr`, as `Scripts/Tags/LockVehicle.sql`), `CountLineItems.sql`, `InsertLineItem.sql` (columns per data-model.md including `@TotalCost`), `UpdateLineItem.sql` (guarded by record, vehicle, and owner), `DeleteLineItem.sql` (guarded by record, vehicle, and owner), `DeleteParts.sql`, `InsertPart.sql`, `ListLineItems.sql` (ordered `created_at, id`, guarded by the owner join), `ListParts.sql` (all parts of the record's line items ordered by line item then `position`)
- [X] T038 [US3] Extend `src/MyGarage.Database/ServiceRecords/ServiceRecordRepository.cs`: `AddLineItemAsync(ownerId, vehicleId, recordId, lineItemId, ServiceLineItemInput input, now, ct)` returning a result (`Created(detail)`, `NotFound`, `LimitReached`) and `UpdateLineItemAsync(ownerId, vehicleId, recordId, lineItemId, input, now, ct)` -> `ServiceRecordDetailRecord?` and `DeleteLineItemAsync(ownerId, vehicleId, recordId, lineItemId, ct)` -> `bool`; each write runs in one transaction that first takes the row lock (`LockRecord`), computes `total_cost` with `ServiceCostCalculator.LineTotal` (stored value is the only place it is computed on write), writes the line, replaces its parts (`DeleteParts` then `InsertPart` with `position` 1..n), and then returns the reloaded detail; update `GetAsync` to load line items and parts (`ListLineItems`, `ListParts`); T032 must pass
- [X] T039 [P] [US3] Implement `src/MyGarage.Api/ServiceRecords/ServiceLineItemRequestValidator.cs` and `ServicePartRequestValidator.cs` with exactly the rules and messages in T033 (reuse `TrimmedText` for `Description` 200 and part `Name` 100; a shared helper for "at most two decimals" such as `decimal.Round(value, 2) == value`; `RuleForEach(request => request.Parts).SetValidator(...)`; the cross-field pricing-mode rules on the request level); T033 must pass
- [X] T040 [US3] Extend `src/MyGarage.Api/ServiceRecords/ServiceRecordMapper.cs` (line item and part mapping per T034, building `ServiceLineItemInput` from the request) and `ServiceRecordsEndpoints.cs`: `POST "/{recordId:guid}/line-items"` (`AddServiceLineItem`, 201 with the whole record, 409 `ProblemDetails` titled "A service record can have up to 50 line items." on `LimitReached`), `PUT "/{recordId:guid}/line-items/{lineItemId:guid}"` (`UpdateServiceLineItem`, 200), `DELETE ...` (`DeleteServiceLineItem`, 204), each with `ValidationFilter<ServiceLineItemRequest>` where it has a body and the `Produces`/`ProducesProblem` metadata from the contract; register the new validators in `AddServiceRecords()`; T034 and T035 must pass
- [X] T041 [P] [US3] Extend `src/MyGarage.Web/ServiceRecords/ServiceRecordsApiClient.cs` with `AddLineItemAsync(Guid vehicleId, Guid recordId, ServiceLineItemRequest, ct)` and `UpdateLineItemAsync(Guid vehicleId, Guid recordId, Guid lineItemId, ServiceLineItemRequest, ct)` (both -> `ApiResult<ServiceRecordDto>`, a 409 carries the problem title through `ApiOutcome.Conflict`) and `DeleteLineItemAsync(Guid vehicleId, Guid recordId, Guid lineItemId, ct)` -> `ApiResult<bool>`; extend `ServiceRecordFormat` with `LaborLine(ServiceLineItemDto)` and `PartLine(ServicePartDto)` text helpers
- [X] T042 [US3] Create the line item pages in `src/MyGarage.Web/ServiceRecords/`: `ServiceLineItemFormModel.cs` (`Description`, `PricingMode` enum `Flat`/`Breakdown`, `FlatCost`, `LaborHours`, `LaborRate`, and a `List<PartRow>` of `Name`/`Quantity`/`UnitPrice`; `ToRequest()` sends only the selected mode and drops all-blank part rows; `From(ServiceLineItemDto)`; `AddBlankPartRows(int count = 3)` up to 25 rows), `ServiceLineItemForm.razor` (labeled inputs and radio pair as in T036, `add-part-rows` handler that re-renders without saving, part error messages mapped from `Parts[i].Field` keys to the visible row number), `ServiceLineItemCreate.razor` (`@page "/vehicles/{VehicleId:guid}/service-records/{RecordId:guid}/line-items/new"`, form name `create-line-item`) and `ServiceLineItemEdit.razor` (`@page ".../line-items/{LineItemId:guid}/edit"`, form name `update-line-item`); post-redirect-get to the record page; render all user text encoded, no `MarkupString`; T036 form tests must pass
- [X] T043 [US3] Update `src/MyGarage.Web/ServiceRecords/ServiceRecordDetail.razor`: render the line item list (Bootstrap list-group, one row per line with description, pricing summary, line total, Edit link, Delete confirmation panel `delete-line-item-{lineItemId}`), the "Add line item" link, and the total cost from `ServiceRecordDto.TotalCost`; update the list rows from T022 only if the API shape changed; T036 detail tests must pass

**Checkpoint**: US3 tests pass; quickstart steps 3, 4, 5, and 7 work.

---

## Phase 6: User Story 4 - Service records stay with their vehicle and stay private (Priority: P1)

**Goal**: Records are visible and changeable only by the vehicle owner, are removed with the vehicle, and leave no contents in logs or addresses.

**Independent Test**: With two users, confirm cross-user and wrong-vehicle requests behave as not found for every endpoint and id, deleting a vehicle removes its records, and logs and URLs carry no record contents.

### Tests for User Story 4 (write first, confirm they fail)

- [X] T044 [P] [US4] Extend `tests/MyGarage.Database.Tests/ServiceRecords/ServiceRecordRepositoryTests.cs` and `ServiceLineItemRepositoryTests.cs` (table-driven over every repository method): another owner's vehicle returns null/false and writes nothing; a record or line item reached through a different vehicle of the same owner behaves as not found; deleting a vehicle through `VehicleRepository` removes its records, line items, and parts (row counts in all three tables are zero for that vehicle) while another vehicle's records stay
- [X] T045 [P] [US4] Extend `tests/MyGarage.Api.Tests/Vehicles/OwnershipIsolationTests.cs` (table-driven over every operation in `contracts/api.openapi.yaml`: `listServiceRecords`, `createServiceRecord`, `getServiceRecord`, `updateServiceRecord`, `deleteServiceRecord`, `addServiceLineItem`, `updateServiceLineItem`, `deleteServiceLineItem`): user B receives 404 for user A's vehicle, record, and line item ids and nothing changes afterwards (reload as user A); user B cannot reach user A's record through B's own vehicle id; deleting a vehicle through `DELETE /api/vehicles/{id}` makes its records 404 and leaves no rows
- [X] T046 [P] [US4] Extend `tests/MyGarage.Api.Tests/Vehicles/LoggingTests.cs`: create and update a record with a distinctive shop name, line items with distinctive descriptions and part names and amounts, and delete them; assert none of those values appear in captured log output
- [X] T047 [P] [US4] Add `tests/MyGarage.Web.Tests/ServiceRecords/ServiceRecordsPrivacyTests.cs`: form posts for create, update, delete, and line items carry no shop, description, part name, or amount in any generated link, form action, or redirect URL (the only query-string text is `tab` and `recordsPage`); every new route redirects an unauthenticated request to sign-in and returns to it (`WebAppFactory`); extend `tests/MyGarage.Web.Tests/Vehicles/VehicleDetailPageTests.cs`: the delete-vehicle confirmation reads "This also removes its tags, journal notes, service records, and pictures and cannot be undone."

### Implementation for User Story 4

- [X] T048 [US4] Update the delete confirmation text in `src/MyGarage.Web/Vehicles/VehicleDetail.razor` per T047, then fix any gap the tests in T044-T047 reveal (owner/vehicle scoping in `src/MyGarage.Database/Scripts/ServiceRecords/*.sql`, log statements in `src/MyGarage.Api/ServiceRecords/`, redirects in `src/MyGarage.Web/ServiceRecords/`); log only ids, counts, and outcomes

**Checkpoint**: US4 tests pass.

---

## Phase 7: User Story 5 - Garage mileage reflects service history (Priority: P2)

**Goal**: Vehicles have an optional Latest Odometer Reading, and the card mileage and mileage filters use the greater of the latest-dated record's odometer, that field, and the existing purchase mileage.

**Independent Test**: Set Latest Odometer Reading 60,000 and see the card show it; add a record at 70,000 and see 70,000; delete it and see 60,000; filters follow the same value.

### Tests for User Story 5 (write first, confirm they fail)

- [X] T049 [P] [US5] Extend `tests/MyGarage.Database.Tests/Vehicles/VehicleRepositoryTests.cs` and `VehicleSearchTests.cs` (and `Common/TestData.cs`): insert/get/update round-trips `latest_odometer_reading` (value and null); `ListPageAsync` summary `Mileage` equals `GREATEST(latest-dated record odometer, latest_odometer_reading, mileage)` ignoring nulls in each combination (all null -> null; only `mileage` 20000 -> 20000; `latest_odometer_reading` 60000 and mileage 20000 -> 60000; record 70000 -> 70000; record 50000 with `latest_odometer_reading` 60000 -> 60000); "latest-dated" means highest `date_serviced`, ties by newest `created_at`: a backdated record with a lower odometer does not lower the value, and a later-dated record with a lower odometer than an earlier one is the one used before `GREATEST`; deleting or updating the latest record changes the value accordingly; `MinMileage`/`MaxMileage` filters and `Count` use the same effective value (a vehicle with record 70000 matches `MinMileage` 65000 and not `MaxMileage` 65000); another owner's records never influence a vehicle
- [X] T050 [P] [US5] Extend `tests/MyGarage.Api.Tests/Vehicles/VehicleRequestValidatorTests.cs`, `VehicleMapperTests.cs`, and `VehiclesEndpointsTests.cs`: `latestOdometerReading` optional whole number 0-9,999,999 (boundaries -1, 0, 9999999, 10000000; message "Latest odometer reading must be between 0 and 9,999,999."); mapped both ways between `VehicleRequest`, `VehicleRecord`, and `VehicleDetailDto`; create and update round-trip it; `GET /api/vehicles` summaries show the effective `mileage` after a service record is added (60,000 -> 70,000 -> back to 60,000 after delete) and the mileage filters use it
- [X] T051 [P] [US5] Extend the Web tests `tests/MyGarage.Web.Tests/Vehicles/VehicleFormTests.cs`, `VehicleFormModelTests.cs`, `VehicleDetailPageTests.cs`, and `VehicleCardTests.cs`: Basic Info has an optional "Latest odometer reading" number input after the existing mileage field (`min="0" max="9999999" step="1" inputmode="numeric"`) with the helper text "Shown on your garage card unless a newer service record has a higher reading."; an API error on it opens the Basic Info tab; the detail page shows it as a `ViewField` ("Not set" when empty); the card still shows the summary mileage formatted as before

### Implementation for User Story 5

- [X] T052 [P] [US5] Add `LatestOdometerReading` (`int?`) to `src/MyGarage.Shared/Vehicles/VehicleRequest.cs` and `VehicleDetailDto.cs`; update the comment on `VehicleSummaryDto.Mileage` in `src/MyGarage.Shared/Vehicles/VehicleSummaryDto.cs` ("the greater of the latest service record's odometer, the latest odometer reading, and the mileage on purchase or lease")
- [X] T053 [US5] Update `src/MyGarage.Database/Vehicles/` and `Scripts/Vehicles/`: `VehicleRecord` gains `LatestOdometerReading`; `Insert.sql`, `Update.sql`, and `GetById.sql` write/read `latest_odometer_reading` (keep `owner_id = @OwnerId`); in `ListPage.sql` and `Count.sql` add `LEFT JOIN LATERAL (SELECT sr.odometer_reading FROM service_records sr WHERE sr.vehicle_id = v.id ORDER BY sr.date_serviced DESC, sr.created_at DESC, sr.id DESC LIMIT 1) lr ON true`, select `GREATEST(lr.odometer_reading, v.latest_odometer_reading, v.mileage) AS mileage`, and replace `v.mileage` in the `@MinMileage`/`@MaxMileage` predicates with the same `GREATEST(...)` expression; T049 must pass
- [X] T054 [US5] Update `src/MyGarage.Api/Vehicles/VehicleRequestValidator.cs` (`RuleFor(request => request.LatestOdometerReading).InclusiveBetween(0, 9_999_999).WithMessage("Latest odometer reading must be between 0 and 9,999,999.")`) and `VehicleMapper.cs` (request, record, and detail mapping); T050 must pass
- [X] T055 [US5] Update the Web: `src/MyGarage.Web/Vehicles/VehicleFormModel.cs` (`LatestOdometerReading` in the model, `ToRequest()`, and `From`), `VehicleFormFields.cs` (add `"LatestOdometerReading"` to `BasicFields`), `VehicleBasicFields.razor` (the labeled input and helper text from T051, `id="latest-odometer-reading"`, `aria-describedby` linking the helper text and the error), and `VehicleDetail.razor` (`<ViewField Label="Latest odometer reading" ... />` after the mileage field in the Basic group, using `VehicleFormat.MileageOrNotSet`); T051 must pass

**Checkpoint**: US5 tests pass; quickstart step 8 works.

---

## Phase 8: User Story 6 - See service history at a glance (Priority: P3)

**Goal**: The Service Records tab shows the record count and the most recent service date.

**Independent Test**: With several records, confirm the count and last serviced date; both update after adding or deleting a record; with none, only the empty state shows.

### Tests for User Story 6 (write first, confirm they fail)

- [X] T056 [P] [US6] Extend `tests/MyGarage.Web.Tests/ServiceRecords/ServiceRecordsViewTests.cs`: with records the tab shows "N service records" (singular "1 service record") from `TotalCount` and "Last serviced {date}" from the first record of page 1, on every page the summary is the same; with no records no summary is shown (the empty state only); a failed list call shows no summary
- [X] T057 [P] [US6] Extend `tests/MyGarage.Api.Tests/ServiceRecords/ServiceRecordsEndpointsTests.cs`: after create and delete the first page's `totalCount` and first item's `dateServiced` reflect the remaining records (the data the summary reads)

### Implementation for User Story 6

- [X] T058 [US6] Add the summary (`<p data-testid="service-summary">`) above the list in `src/MyGarage.Web/ServiceRecords/ServiceRecordsView.razor` using `ServiceRecordFormat` (a pluralization helper for "service record(s)"); T056 and T057 must pass

**Checkpoint**: US6 tests pass; quickstart step 6 summary works.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T059 [P] Extend `tests/MyGarage.Web.Tests/Common/UiStandardConformanceTests.cs` so it also scans `src/MyGarage.Web/ServiceRecords/*.razor` (no `MarkupString`, no `<table>` for the record and line item lists, no inline `style` or jQuery, icon-only controls have accessible names)
- [X] T060 [P] Update `docs/ui-standard.md`: the vehicle tab list gains Service Records (wrench icon); add "Service record" patterns (record list rows, record page with line items and total cost, line item form with the flat/breakdown radio pair and part rows, `$` money formatting); the vehicle Basic Info field list gains Latest odometer reading; keep `UiStandardConformanceTests` consistent with the document
- [X] T061 [P] Update `specs/005-service-records/contracts/web-routes.md` and `quickstart.md` only if implementation differs (final label or message text); update `src/README.md` and `tests/README.md` if they list features or test folders (`ServiceRecords/`); update the About and FAQ help text in `src/MyGarage.Web/Help/About.razor` and `Faq.razor` to mention service records, with matching updates in `tests/MyGarage.Web.Tests/Help/`
- [X] T062 [P] Accessibility and presentation pass on `src/MyGarage.Web/ServiceRecords/*.razor` and the changed vehicle components: keyboard order, labels and `aria-describedby` on every input including part rows (row number in the accessible name, for example "Part 2 quantity"), accessible names on Edit/Delete buttons that include the record date or line description, `prefers-reduced-motion` respected, narrow-screen layout without page-level horizontal scroll; confirm no `MarkupString`, no jQuery, and no new custom JavaScript
- [X] T063 Run `dotnet build MyGarage.slnx` (zero warnings) and `dotnet test MyGarage.slnx` with Docker running; fix anything failing, including pre-existing tests affected by the changed summary mileage and the new migrations
- [X] T064 Run the full quickstart in `specs/005-service-records/quickstart.md` (manual section; sign-in steps use the integrated browser after asking the user to sign in); confirm migrations 0009 and 0010 apply once on the existing local database, existing vehicles and their card mileage are unchanged, and fix any gaps found

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies; start immediately
- **Foundational (Phase 2)**: depends on Setup; BLOCKS all user stories (schema, DTOs, calculator, persistence types, test helpers, icon)
- **US1 (Phase 3)** depends on Foundational only; it is the MVP
- **US2 (Phase 4)** depends on US1 (record pages, repository, endpoints group)
- **US3 (Phase 5)** depends on US1 (the record page and repository); it extends the files US2 also edits (`ServiceRecordRepository.cs`, `ServiceRecordsEndpoints.cs`, `ServiceRecordsApiClient.cs`, `ServiceRecordDetail.razor`), so do US3 after US2 or merge carefully
- **US4 (Phase 6)** depends on US1-US3 (it verifies the finished endpoints, pages, and cascade)
- **US5 (Phase 7)** depends on Foundational and US1 (its SQL reads `service_records`); independent of US2-US4, but it shares `VehicleDetail.razor` with US1 and US4
- **US6 (Phase 8)** depends on US1 (it edits the tab view)
- **Polish (Phase 9)** depends on all stories being complete

### Within Each User Story

- Tests first and failing, then SQL, then repository, then Api validators/mappers/endpoints, then Web client and pages
- Tasks that edit the same file are not parallel with each other: `ServiceRecordRepository.cs` (T017, T028, T038), `ServiceRecordsEndpoints.cs` (T019, T029, T040), `ServiceRecordsApiClient.cs` (T020, T030, T041), `ServiceRecordDetail.razor` (T023, T031, T043), `ServiceRecordMapper.cs` (T019, T040), `ServiceRecordsView.razor` (T022, T058), `VehicleDetail.razor` (T022, T048, T055), `VehicleMapper.cs` and `VehicleRequestValidator.cs` (T054), `ListPage.sql` and `Count.sql` for vehicles (T053), `Program.cs` files (T019, T020)

### Story completion order

Foundational -> US1 -> US2 -> US3 -> US4; US5 and US6 can follow US1 in any order; Polish last

### Parallel Opportunities

- Foundational: T002 and T003 together (test files); T004, T005, T006, T007, T008, T009, T010 together (different files)
- US1: T011-T015 together (test files); T018 and T020 together after the tests
- US2: T024-T026 together; T030 alongside T028/T029
- US3: T032-T036 together; T039 and T041 together
- US4: T044-T047 together
- US5: T049-T051 together; T052 alongside the SQL work
- US6: T056 and T057 together
- Polish: T059-T062 together

### Parallel Example: User Story 1

```text
Together: T011, T012, T013, T014, T015 (all test files)
Then: T016 -> T017 -> T019 (with T018 in parallel) -> T020 -> T021 -> T022 -> T023
```

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 and Phase 2
2. Complete Phase 3 (US1) and validate with quickstart steps 1 and 2
3. Demo: users can add records to a vehicle and see them on the Service Records tab, newest first

### Incremental Delivery

1. Add US2 and US3: records can be corrected and deleted, and carry itemized line items with a total cost (the three P1 stories that deliver the requested service record experience)
2. Add US4 (privacy and cascade verification) before any release
3. Add US5 (garage mileage from service records and Latest Odometer Reading) and US6 (history summary)
4. Finish with Polish (UI standard doc, accessibility, full test run, quickstart)

## Notes

- `[P]` tasks touch different files with no incomplete dependencies
- Each story's checkpoint is an independently demonstrable increment
- Quote and keep the validation limits exactly as written; if a limit must change, update `data-model.md` and `research.md` R9 first
- Never edit migrations `0001`-`0008`; add new numbered scripts only
- Commit after each task or logical group on branch `005-service-records`; Conventional Commits
