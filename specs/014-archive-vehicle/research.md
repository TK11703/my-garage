# Research: Archive Vehicle

Phase 0 decisions for [plan.md](plan.md). The spec has no open clarifications; these records settle design choices left open by the spec and the planning input (status filter on the list, reminders disabled not deleted, fuel and charging keep archived vehicles).

## Codebase findings that shape the design

- Every child feature (service records, fuel and charging, inspections, registrations, insurance, taxes, documents, images, notes, tags, reminders) is mounted under `/api/vehicles/{vehicleId:guid}/...` with its own `MapGroup`, and its SQL proves ownership through `vehicles.owner_id`. There is no single place that decides "may this vehicle be written to".
- `GET /api/vehicles` already takes a validated query record (`VehicleListQuery`) and `ListPage.sql`, `Count.sql`, and `FilterOptions.sql` repeat the same owner-scoped predicate; the Web `VehicleListCriteria` carries the filters in the page address and `/vehicles` forwards search keys to `/search`.
- Reminders already have a per-reminder `is_paused` the user controls (`SetPaused.sql`). `ReminderStatusCalculator` is the single definition of status; `ListForEvaluation.sql` already skips paused reminders, so the notifier needs no new code path to skip archived ones.
- `ListForOwner.sql` feeds only the garage-wide reminders list and the dashboard, so excluding archived vehicles there covers every "you have X reminders" figure.
- `Dashboard/Counts.sql`, `RecentActivity.sql`, and `FuelEnergySpend.sql` are owner-scoped statements joined to `vehicles`, so each decision below is a one-line predicate per script.
- `Web/Vehicles/ApiResult.cs` already has `ApiOutcome.Conflict`; the API already answers problem details for every refusal.
- Draft review lists the owner's vehicles through `Drafts/ListOwnerVehicles.sql` and reads a draft's vehicle through a `LEFT JOIN vehicles` in `ListOpenPage.sql` and `GetById.sql`.
- Registrations, inspections, insurance, and taxes carry expiration or next-due dates but compute no status and raise no notices (feature 006, feature 013 research). Reminders are the only due-date mechanism, so "tax due" and "registration due" are reminders and are covered by disabling reminders.

## Decisions

### D1. Archived state is one nullable timestamp on the vehicle

- **Decision**: `vehicles.archived_at timestamptz NULL` (migration `0018_add_vehicle_archive.sql`). `NULL` means active; a value is the instant it was archived. The DTOs carry it as `ArchivedAt`. The Web shows its calendar date in UTC, like every other date in the app.
- **Rationale**: One column answers "is it archived" and "since when" (FR-001, FR-013) with no second field that can disagree, and every existing row is active with no backfill (FR-014). A partial index on active vehicles keeps the default list fast.
- **Alternatives considered**: A boolean plus a date column (two fields to keep consistent); a separate `archived_vehicles` table (moves rows and breaks every foreign key, so history would have to be copied); a status enum (only two states exist and a third is not planned).

### D2. Archive and restore are explicit state-change endpoints

- **Decision**: `POST /api/vehicles/{vehicleId}/archive` and `POST /api/vehicles/{vehicleId}/restore`, each returning the updated `VehicleDetailDto`. Each is one conditional `UPDATE ... WHERE id AND owner_id AND archived_at IS [NOT] NULL RETURNING`, setting `updated_at` and `updated_by` (FR-012). No row means the vehicle is not the caller's (404) or already in the target state (409 with a problem naming the state), distinguished by one follow-up read of the caller's own vehicle. Delete is unchanged and works for either state.
- **Rationale**: State changes are commands, not field edits: they must not ride on `PUT /api/vehicles/{id}`, whose request is the full vehicle form and whose validator would be bypassed or duplicated. The conditional update makes two concurrent archives safe (one wins, one sees 409).
- **Alternatives considered**: `PATCH` with `archived: true` (invites arbitrary partial updates to a form-shaped resource); `PUT /archive` with a body flag (no body is needed); making archive idempotent (hides a stale second tab from the user, which the spec asks to report).

### D3. Read-only is enforced once, by an endpoint filter, and proven by a route-walking test

- **Decision**: `RequireActiveVehicleFilter` (in `MyGarage.Api/Vehicles`) is an endpoint filter added with `.RequireActiveVehicleForWrites()` to each vehicle-scoped group: fuel and charging, inspections, insurance, registrations, taxes, service records, reminders, documents, images, notes, and tags, and to `PUT /api/vehicles/{vehicleId}`. For GET, HEAD, and OPTIONS it passes straight through. For any other method it reads the caller's own vehicle's archived state (`Vehicles/GetArchivedState.sql`, scoped by `owner_id`); an archived vehicle gets `409 Conflict` with a `ProblemDetails` whose type is `urn:mygarage:problem:vehicle-archived`; a vehicle that is absent or not the caller's falls through to the endpoint's normal 404, so nothing is disclosed (FR-010). Archive, restore, and `DELETE /api/vehicles/{vehicleId}` are not guarded.
  A conformance test enumerates the API's endpoint data source and fails if any non-read route under `/api/vehicles/{vehicleId}` other than archive, restore, and delete lacks the filter. New child features therefore cannot silently skip it.
- **Rationale**: Ten-plus features each own their SQL; patching every `Insert`, `Update`, and `Delete` script would be a large diff, would answer an archived write as a misleading 404 (the spec wants a clear message), and would still let a newly added feature forget. A filter gives one check, one status code, and a test that guards the whole surface (FR-007, SC-006). The check-then-write gap is harmless: a write that lands just before an archive is the same as a write made a moment earlier.
- **Alternatives considered**: `AND v.archived_at IS NULL` in every write script (many scripts, wrong 404, easy to forget); a database trigger (logic outside the C# suite and against the repo's SQL-in-files convention, as in feature 013); one repository wrapper (the repositories are per-feature by constitution II).

### D4. Archiving disables reminders by derivation, not by rewriting them

- **Decision**: A reminder is disabled because its vehicle is archived; nothing in `reminders` changes on archive or restore. `ReminderStatus` gains `Archived` (`archived` on the wire; shown as "Disabled, vehicle archived"). `ReminderCalculationInput` gains `VehicleArchived`, which takes precedence over `IsPaused`, so `ReminderStatusCalculator` returns `Archived` with no next-due urgency. The SQL that reads reminders returns `vehicle_archived`.
  - `ListForEvaluation.sql` adds `AND v.archived_at IS NULL`, so the notifier never raises a notice for an archived vehicle.
  - `ListForOwner.sql` adds `AND v.archived_at IS NULL`, so the garage-wide reminders list, its status counts, the dashboard's overdue and due-soon counts, and the Needs attention list never include them.
  - `ListForVehicle.sql` and `GetById.sql` keep archived vehicles' reminders and show them as `Archived` on the vehicle's own Reminders tab (they are history there).
  - `ReminderOrdering` places `Archived` after `Paused`.
- **Rationale**: Restore then needs no work and is exactly lossless: a reminder the user paused themselves stays paused, notified state and due points are untouched (FR-005, SC-008). Tax-due and registration-due reminders are ordinary reminders, so they are covered with no special case. Excluding at the two list queries (rather than filtering in C#) keeps every count and list consistent because they all read the same two scripts.
- **Alternatives considered**: Setting `is_paused = true` on every reminder at archive (restore cannot tell which were paused by the user without a second column, and it rewrites rows); deleting reminders (violates the request); a new `disabled_by_archive` column (a stored copy of a derivable fact that can go stale).

### D5. Waiting reminder notices are marked read on archive; restore re-evaluates

- **Decision**: In the same transaction as the archive update, `Notifications/MarkReadForVehicleReminders.sql` sets `read_at` on the vehicle's unread reminder notifications (owner-scoped through the reminders and vehicles join). Restore calls `ReminderEvaluator.TryEvaluateVehicleAsync` after the update, as the other status-changing writes do. `notified_state` is untouched, so a reminder already notified at a state is not notified again for it, but one that has worsened while archived raises one notice on restore.
- **Rationale**: Meets "notices already waiting are cleared" without deleting history the user can still dismiss with the existing "clear read" action, consistent with feature 013 D8. The evaluator hook keeps restore symmetrical with every other write that can change a status.
- **Alternatives considered**: Deleting the notifications (loses the read history); leaving them unread (a bell notice for a car the user no longer owns); resetting `notified_state` on restore (re-notifies things the user already saw).

### D6. Dashboard: one rule, fuel and charging keep archived vehicles

- **Decision**: In `Dashboard/Counts.sql` the vehicle count and the service record count add `archived_at IS NULL`; the fuel and charging entry count does not. `FuelEnergySpend.sql` is unchanged in its filter and additionally returns `v.archived_at IS NOT NULL AS vehicle_archived`; `FuelEnergySpendVehicleDto` gains `VehicleArchived` and the chart legend and tooltips append "(archived)" to the vehicle's name. `RecentActivity.sql` adds `archived_at IS NULL`. Reminder counts and the attention list follow D4. The drafts-awaiting-review count is unchanged (a draft belongs to the user, not a vehicle).
  The rule for later metrics is written into the spec (FR-004): fuel and charging calculations keep archived vehicles; every other garage-wide figure leaves them out.
- **Rationale**: This is the planning input: money spent on fuel and charging really was spent, so removing an archived car's months from the chart would rewrite history, while a count of owned vehicles or an attention list for a car that is gone is noise. Counting entries with the spend keeps the entry count and the chart from disagreeing.
- **Alternatives considered**: Excluding archived vehicles from the entry count (the card and the chart would disagree); an include-archived toggle on the dashboard (extra control nobody asked for); counting archived service records (contradicts "remove it from measured metrics" for everything but fuel and charging).

### D7. The list gets a `status` filter, defaulting to active

- **Decision**: `VehicleListQuery` gains `Status` (`active` default, `archived`, `all`), validated, passed to `ListPage.sql`, `Count.sql`, and `FilterOptions.sql` as one text parameter with the predicate `(@Status = 'all' OR (@Status = 'active' AND v.archived_at IS NULL) OR (@Status = 'archived' AND v.archived_at IS NOT NULL))`. `VehicleSummaryDto` and `VehicleDetailDto` gain `ArchivedAt`. Filter choices (make, color, tags, and so on) follow the selected status so a choice never yields an empty result. In the Web, `VehicleListCriteria` gains `Status` (omitted from the query string when active), `/vehicles` and `/search` show a three-choice link group (Active, Archived, All), paging and the other criteria keep the choice, and on `/search` a non-default status is a removable badge like the other criteria.
- **Rationale**: Matches the planning input ("filter active vehicles from archived vehicles") with the existing list, search, paging, and badge machinery and no new page. Active as the default means nothing changes for users who never archive (FR-003, FR-006).
- **Alternatives considered**: A separate Archived page (more surface, duplicates the list and its filters); a Boolean `archived` parameter (cannot express All); a client-side filter (would break paging and the counts).

### D8. Web: read-only vehicle page, one place to restore

- **Decision**: `VehicleDetail.razor` for an archived vehicle shows an "Archived on {date}" alert and badge, replaces Edit and the Add menu with **Restore** (a `details` confirm panel like Delete), keeps **Delete** (its text unchanged), and supplies a cascading `VehicleArchivedState` so every tab section hides its add, edit, delete, upload, and import controls. For an active vehicle the page gains an **Archive** `details` confirm panel next to Delete, and the Delete confirmation text adds "To keep the history instead, archive the vehicle." A write page reached by address for an archived vehicle (add or edit under a vehicle) shows the standard "This vehicle is archived" message with a Restore link, driven by the API's `vehicle-archived` problem type mapped to a new `ApiOutcome.Archived` in the shared response handling.
- **Rationale**: Static SSR means no interactive hiding; the cascading value is the lightest way to cover every section without threading a parameter through all of them. Mapping the 409 once keeps the many write pages from each learning about archiving.
- **Alternatives considered**: A parameter on every section component (large diff, easy to miss one); disabling controls instead of hiding them (clutter and a false promise of editability).

### D9. Drafts treat an archived vehicle as no vehicle

- **Decision**: `ListOwnerVehicles.sql` adds `archived_at IS NULL`, so review pickers and the intake matcher never offer or propose an archived vehicle. `ListOpenPage.sql` and `GetById.sql` return the draft's `vehicle_id` and name parts as null when the vehicle is archived (the `LEFT JOIN` gains `AND v.archived_at IS NULL` and the id is selected through the joined row), so such a draft shows as needing a vehicle. Including a draft into an archived vehicle (a stale form) is refused through the existing "that vehicle is not one of yours" validation path with an archived-aware message, and "Import Record" and "Add Maintenance" are hidden for archived vehicles by D8.
- **Rationale**: Reuses the unmatched-draft flow (FR-011, spec Story 5) with no new draft state and no migration of draft rows.
- **Alternatives considered**: Clearing `vehicle_id` on drafts at archive time (rewrites rows and cannot be undone on restore); letting a draft include into an archived vehicle (violates read-only).

### D10. Unchanged on purpose

- **VIN uniqueness** still covers archived vehicles, so a VIN cannot be added twice; the duplicate message tells the user to look for the vehicle under Archived and restore it. No migration of constraints.
- **Lookups usage** (a lookup item in use by a vehicle) still counts archived vehicles, so a lookup an archived vehicle uses cannot disappear from under its history.
- **Sample data** has no archived vehicles; a test builds one.
- **No new package, project, container, or infrastructure.**

### D11. Observability and privacy

- **Decision**: Two counters, `vehicles.archived` and `vehicles.restored`, in the existing vehicles meter. Logs carry vehicle and owner ids and the outcome only; no vehicle name, VIN, or record data (FR-012). The audit trail is `updated_at` and `updated_by` on the vehicle row.
- **Alternatives considered**: A separate audit table (the repo has none for other changes; attribution by `updated_by` is the existing convention).

## Resolved clarifications

None required. Every `NEEDS CLARIFICATION` item in the Technical Context is filled in.
