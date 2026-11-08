# Research: Vehicle Service Records

All technical context was resolved from the existing code base and the product owner's field list; there were no open `NEEDS CLARIFICATION` items. Each decision follows the patterns of features 002 (journal notes) and 004 (lookups, expanded vehicle details).

## R1. Feature placement

- **Decision**: A new top-level `ServiceRecords/` feature folder in `MyGarage.Shared`, `MyGarage.Database` (plus `Scripts/ServiceRecords/`), `MyGarage.Api`, and `MyGarage.Web`, mirrored in each test project. The vehicle changes (Latest Odometer Reading, effective mileage) stay in `Vehicles/`.
- **Rationale**: Constitution II names `ServiceRecords/` explicitly. Journal notes sit under `Vehicles/` only because they are small; service records have three tables, nine endpoints, and several pages.
- **Alternatives**: Nest under `Vehicles/ServiceRecords/` like notes. Rejected: the feature is large enough to deserve its own folder, and a cleaner boundary makes removal easy.

## R2. Ownership enforcement

- **Decision**: Service tables carry no `owner_id`. Every statement joins up to `vehicles` and filters `vehicles.owner_id = @OwnerId` (same as `JournalNotes/*.sql`). Line item and part statements join `service_line_items` -> `service_records` -> `vehicles`, and always include the `vehicleId` and `recordId` from the route so a record reached through the wrong vehicle behaves as not found.
- **Rationale**: Constitution requires ownership verification through the parent vehicle and owner-scoped SQL. A duplicated `owner_id` column could drift.
- **Alternatives**: Denormalize `owner_id` onto every table. Rejected: more writes and a drift risk for no benefit at this scale.

## R3. Data model shape

- **Decision**: Three tables: `service_records` (metadata), `service_line_items` (what was done and its cost), `service_line_item_parts` (parts for a breakdown line). All child foreign keys use `ON DELETE CASCADE`, so deleting a vehicle removes its records (FR-011) without extra code. Parts are not exposed by id; they are always read and replaced as a set with their line item.
- **Rationale**: Matches the requested shape (record -> line items -> optional labor/parts breakdown). Parts need many rows per line item, so a child table is required; labor is one pair of columns per line item.
- **Alternatives**: Store parts as a JSON column. Rejected: constitution favors plain relational SQL, and JSON would complicate totals and constraints.

## R4. Line item pricing modes

- **Decision**: A line item is priced in exactly one of two ways: a **flat cost**, or a **breakdown** of labor (hours and an hourly rate) and/or parts (name, quantity, price per part). Both at once is rejected. A line item must have one of them; a free service is a flat cost of 0.00. Enforced by FluentValidation and mirrored by a table check constraint.
- **Rationale**: The request says the user "can provide a cost for the service or can break down the service". Making the modes exclusive prevents double counting, and the check constraint protects the data if a validator is ever bypassed.
- **Alternatives**: Allow a flat amount plus a breakdown (for example fees on top). Rejected as out of scope and ambiguous in the total.

## R5. Labor needs an hourly rate

- **Decision**: Labor is entered as **hours** and an **hourly rate** (both required together). Labor cost = hours x rate.
- **Rationale**: The request lists "how many hours of labor" without a rate, but a cost cannot be shown or totaled from hours alone. This is called out for the product owner to confirm.
- **Alternatives**: Hours as a note with no cost. Rejected: the total would be incomplete.

## R6. Where totals are computed

- **Decision**: One pure `ServiceCostCalculator` (in `MyGarage.Database/ServiceRecords/`) computes a line item's total: flat cost, or `round(hours x rate, 2) + sum(round(quantity x unit price, 2))`, using `MidpointRounding.AwayFromZero`. The repository calls it on every line item write and stores the result in `service_line_items.total_cost`. A record's total is `SUM(total_cost)` computed in SQL (list and detail). Part totals shown in the UI use the same calculator in the API mapper.
- **Rationale**: One calculation, one rounding rule, no drift between list totals and detail totals; the list can total many records in a single query without loading line items. The write path is the single repository method, so the stored value cannot go stale.
- **Alternatives**: Compute every total at read time in C#. Rejected: the paged list would need all line items and parts loaded. Compute totals in SQL expressions only. Rejected: rounding rules duplicated across several scripts.

## R7. Line item and parts API shape

- **Decision**: Service record metadata and line items are separate resources: `POST /api/vehicles/{vehicleId}/service-records` creates the record (metadata only); line items are added, edited, and deleted under `/service-records/{recordId}/line-items`. A line item request carries its parts as a list; an update replaces the whole parts list in one transaction. Limits: 50 line items per record (409 beyond that) and 25 parts per line item (400 beyond that).
- **Rationale**: The request describes creating the record first and then "create line items". Static SSR pages work best with one small form per concern (as with notes and tags), and nesting parts avoids a part-level API.
- **Alternatives**: One request that creates the record with all line items. Rejected: a much larger SSR form with nested dynamic rows and a heavier validation surface. Part-level endpoints. Rejected: more endpoints and ownership paths for no user benefit.

## R7a. Line item form in static SSR

- **Decision**: The line item form shows the existing parts, or one blank part row when there are none. A "+ Add another part row" submit button (named `add-part-rows`, posted with the same form so the page can tell it from Save) re-renders the page with one more blank row without saving, up to 25, and each row has an X submit button (`remove-part-row`, value = row position) that removes it the same way. Rows left entirely blank are ignored on save; a partly filled row is a validation error.
- **Rationale**: Keeps the page static SSR with post-redirect-get like every other page, with no new JavaScript.
- **Alternatives**: Interactive Server component for the editor. Rejected: breaks the established page pattern for one form.

## R8. Latest Odometer Reading and the garage mileage

- **Decision**: Add a nullable manual vehicle field **Latest Odometer Reading** (0-9,999,999). The mileage on the garage card, and the garage minimum/maximum mileage filters, use `GREATEST(odometer of the latest-dated service record, Latest Odometer Reading, existing Mileage)`; `GREATEST` ignores nulls and returns null (shown as "Mileage not set") only when all three are null. "Latest-dated" means highest Date Serviced, ties broken by newest created. The existing `mileage` column (mileage on the purchase or lease date) is untouched.
- **Rationale**: The request says the card gets its mileage "from the latest Service Record or another field called Latest Odometer Reading". Taking the greater value is predictable without a timestamp on the manual field: a newer service record raises the figure automatically, and a higher manual reading wins when the user updates it. Including the purchase mileage as a floor means existing vehicles keep the number they show today. A backdated record never lowers the figure because the latest-dated record is used.
- **Alternatives**: Records always win. Rejected: a stale record would hide a newer manual reading. Copy the manual value into the vehicle whenever a record is saved. Rejected: duplicate state to keep in sync across edit and delete.

## R9. Date and number rules

- **Decision**: Date Serviced is required, not in the future (compared with the UTC date, as the existing purchase date rule does), and not before 1900-01-01. Odometer Reading is a required whole number 0-9,999,999. Shop Name or Person is optional free text, 1-100 characters after trimming (blank becomes "Not set"), no control characters. Money amounts are 0.00-9,999,999.99 with at most two decimals; labor hours 0.01-999.99; hourly rate 0.00-99,999.99; part quantity 0.01-9,999.99; line description 1-200 characters; part name 1-100 characters.
- **Rationale**: Reuses the existing `TrimmedText` validator helper and date convention; ranges fit `numeric`/`integer` columns with matching check constraints.
- **Alternatives**: Allow future-dated records for planned work. Rejected: scheduling/reminders are out of scope.

## R10. Currency display

- **Decision**: All amounts are in one currency shown with a `$` sign and two decimals (for example `$1,234.50`) using the invariant culture, consistent with the invariant-culture formatting already used for dates and mileage. No currency column is stored.
- **Rationale**: Single-currency personal app; avoids per-user locale handling nobody asked for.
- **Alternatives**: Culture-sensitive currency. Rejected: output would vary by server culture and make tests brittle.

## R11. Concurrency

- **Decision**: Adding a line item locks the parent record row (`SELECT ... FOR UPDATE` in a transaction, as the tag and image repositories lock their vehicle) before counting existing line items, so the 50-item limit cannot be exceeded by concurrent requests. Updating a line item replaces its parts in a single transaction.
- **Rationale**: Established pattern in `Scripts/Tags/LockVehicle.sql` and `Scripts/VehicleImages/LockVehicle.sql`.
- **Alternatives**: Serializable isolation. Rejected: heavier than a row lock for this one invariant.

## R12. Paging

- **Decision**: The record list reuses `PagingQuery`, `PagedResultDto<T>`, and the `Pager` component with default page size 10 (maximum 50) and the same clamping of an out-of-range page as journal notes. The Service Records tab summary uses `TotalCount` and the first record of page 1 for "last serviced", so no extra summary endpoint is needed.
- **Rationale**: Reuses proven pieces; SC-003 (500 records) is met by the index `(vehicle_id, date_serviced DESC, created_at DESC, id DESC)`.
- **Alternatives**: Load all records. Rejected by spec edge cases.

## R13. UI

- **Decision**: Add a fourth tab **Service Records** (`?tab=service-records`) to the vehicle detail page's shared tab strip. Add pages for adding, viewing, and editing a record and for adding and editing a line item (see [contracts/web-routes.md](contracts/web-routes.md)). Mutations are antiforgery-protected form posts with post-redirect-get; deletes use the existing `<details>` confirmation panel. A `Wrench` icon is added to `Icon`/`IconName`. The vehicle delete confirmation text now also mentions service records.
- **Rationale**: Follows `docs/ui-standard.md` and the existing vehicle pages; no new JavaScript or package.
- **Alternatives**: Modal dialogs. Rejected: not used elsewhere and need JavaScript.

## R14. Testing

- **Decision**: Test-first per the constitution: migration tests (empty database, tables, constraints, cascade), repository tests (owner isolation, wrong-vehicle access, ordering, totals, line item and parts replace, limits, effective mileage), `ServiceCostCalculator` unit tests, validator tests (boundaries), endpoint tests (success, validation failure, not found, 401, cross-user, 409 limit), mapper tests, and Web tests (tab, list, record page, forms, delete confirmation, UI conformance).
- **Rationale**: Constitution VIII; real PostgreSQL through Testcontainers.
- **Alternatives**: None permitted for database tests.

## R15. Dependencies

- **Decision**: No new packages, projects, or infrastructure.
- **Rationale**: Everything needed (Dapper, Npgsql, FluentValidation, Bootstrap) is already present.
