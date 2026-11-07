# Research: Service Reminders

Phase 0 decisions for [plan.md](plan.md). The spec had no open clarifications; these records settle design choices left open by the spec and the planning input.

## Codebase findings that shape the design

- The vehicle list already defines the garage mileage as `GREATEST(latest-dated service record odometer, vehicles.latest_odometer_reading, vehicles.mileage)` (`Scripts/Vehicles/ListPage.sql`); `GREATEST` ignores nulls.
- `notifications` has `kind IN ('draft_ready')`, a nullable `draft_id` that cascades, and the bell, dismiss, clear-read, and unread-count flows built on it (`MyGarage.Database/Notifications`, `MyGarage.Web/Notifications/NotificationsMenu.razor`).
- Registrations, inspections, and the other maintenance kinds (feature 006) are *records of what happened*, with their own expiration or next-due dates. Reminders are forward-looking and recur; the two are deliberately not linked (spec Assumptions).
- `DraftExpiryService` is the pattern for a periodic `BackgroundService` with an `Enabled` switch and a public `RunOnceAsync` that tests call directly.
- The Web app is static SSR: no timers, no JavaScript interop in layouts, and forms are named SSR forms with `@onsubmit` handlers. Pages follow the registrations set (`RegistrationList`, `RegistrationForm`, `RegistrationCreate`, `RegistrationEdit`).

## Decisions

### D1. Status is derived, never stored, and computed in one C# calculator

- **Decision**: `ReminderStatusCalculator` (pure, in `MyGarage.Api/Reminders`) takes the reminder, the vehicle's current mileage, and today's date and returns the status, next due mileage and date, miles remaining, and days remaining. Every read path (vehicle tab, garage list, dashboard) and the evaluator call it.
- **Rationale**: One definition of "due soon" and "overdue" with a unit-test table (SC-005). Storing status would go stale the moment the date or the mileage moves.
- **Alternatives considered**: Computing in SQL (duplicates the rule per query and makes the dashboard counts and the notifier able to disagree); storing status and refreshing it on a timer (stale between runs, violates SC-007).

### D2. Rules for status

- Next due mileage = last-done mileage + distance interval. Next due date = last-done date + time interval, using `DateOnly.AddMonths`, which clamps to the last day of a shorter month (spec edge case: 1 year after 29 February).
- A part is **Overdue** when current mileage >= next due mileage or today >= next due date, **Due soon** when within 500 miles or 30 days (inclusive) of its due point, otherwise **Upcoming**. A reminder with both parts takes the more urgent part's state. Overdue is "reached or passed", so due on today's date is Overdue.
- No current mileage and a distance-only reminder: status **Mileage needed**. With both parts, the date part decides and the distance part is skipped until a mileage exists.
- A paused reminder is **Paused** and skips all of the above.
- The thresholds are public constants in `MyGarage.Shared` (`ReminderThresholds`) so the Web can emphasize the part that is at or past its threshold; making them configurable is out of scope (spec Assumptions).

### D3. Time interval stored in months

- **Decision**: `reminders.interval_months` (1 to 600). The request carries a value and a unit (`months` or `years`; years 1 to 50) and the Api converts; responses return the value in years when the month count divides by 12, otherwise months.
- **Rationale**: One numeric column keeps the calculation trivial and avoids a unit column that could disagree with the value. "Every 2 years" and "every 24 months" are the same reminder.
- **Alternatives considered**: Storing value and unit (two columns that must stay consistent); storing days (wrong for calendar months and years).

### D4. What a reminder applies to (superseded)

- **Superseded (2026-11-06)**: the product owner asked to drop the applies-to field. Migration `0017_drop_reminder_applies_to` removes `applies_to` and `subject` and folds any subject into the reminder's name. The original decision follows for the record.

- **Decision**: `applies_to` is `vehicle`, `equipment`, or `location`, plus a `subject` text (up to 100 characters) that is required for equipment and location and null for vehicle. The name of the reminder stays free text ("Annual inspection"); the subject says what it is about ("Trailer hitch", "Home garage permit").
- **Rationale**: Directly answers the planning input that a reminder may be controlled by the vehicle, its equipment, or where it resides, while still belonging to exactly one vehicle. A fixed three-value list is enough; subjects are not a separate entity because nothing else references them.
- **Alternatives considered**: Separate equipment and location tables (premature; nothing else needs them yet); a free-text category (not filterable, no validation).

### D5. Suggested types are form presets, not a table

- **Decision**: `ReminderPresets` in `MyGarage.Shared` lists Oil change (5,000 miles or 6 months), Tire rotation (7,500 miles), Inspection (1 year), Registration (1 year), each applying to the vehicle. Choosing one fills the form with editable values; Custom leaves it empty. The server stores only what is submitted.
- **Rationale**: Matches the spec ("fill in a typical interval the user can change") with no lookup table or admin surface, and follows how `FeatureCategories` is a fixed Shared list.
- **Alternatives considered**: A user-editable lookup list (the existing `Lookups` feature exists, but nothing here needs per-user presets yet).

### D6. Current mileage rule

- **Decision**: The vehicle's current mileage for status is the same greatest-of rule as the vehicle list, computed in `ListForVehicle.sql`, `ListForOwner.sql`, and `ListForEvaluation.sql` by a shared lateral subquery (the latest-dated service record's odometer, the vehicle's latest odometer reading, and its purchase mileage).
- **Rationale**: Spec FR-008 requires the same mileage the garage shows. The rule is one line of SQL; copying it is cheaper than a view and is covered by a test that compares the reminder's mileage with the vehicle list's.
- **Alternatives considered**: A database view (adds a schema object whose definition would have to track the list query); computing in C# from three separate reads (extra round trips for no benefit).

### D7. Marking done

- **Decision**: `POST .../reminders/{id}/complete` with `doneOn`, optional `mileage`, and `updateVehicleMileage`. One transaction inserts a `reminder_completions` row, sets `last_done_on` and `last_done_mileage`, resets `notified_state`, and (only when asked, and only when the mileage is above the vehicle's current mileage) raises `vehicles.latest_odometer_reading`. Mileage is required when the reminder has a distance interval, must not be negative, and must not be below the previous last-done mileage; `doneOn` must not be in the future. Time-only reminders may omit the mileage and then keep their previous last-done mileage.
- **Rationale**: Gives the history (FR-012) without an event table for creation. The vehicle's mileage is never changed without the user's explicit choice (FR-013) and never lowered.
- **Alternatives considered**: Always raising the vehicle mileage (violates FR-013); recording the initial last-done as a completion (invents history the user did not report).

### D8. Notifications reuse the existing table

- **Decision**: Migration 0016 replaces the `kind` check with `draft_ready`, `reminder_due_soon`, `reminder_overdue`, and adds `reminder_id uuid NULL REFERENCES reminders (id) ON DELETE CASCADE`. `NotificationDto` gains `ReminderId` and `VehicleId` (the vehicle comes from joining `reminders` in `List.sql`), and the bell links reminder notifications to `/vehicles/{vehicleId}/reminders/{reminderId}`. Marking done, pausing, or editing a reminder back to Upcoming marks its notifications read (`MarkReadForReminder.sql`); deleting a reminder or its vehicle removes them through the cascade.
- **Rationale**: One bell, one badge, one dismiss flow (FR-014, FR-016, FR-018). Marking read rather than deleting keeps the history the user can still see and clear with the existing "clear read".
- **Alternatives considered**: A separate reminders-alerts table and menu (two bells to maintain); computing notifications at read time (no per-user read or dismiss state).

### D9. One notification per state change: `notified_state`

- **Decision**: `reminders.notified_state` (`NULL`, `due_soon`, or `overdue`) records the last state the user was told about. The evaluator notifies only when the computed state ranks above `notified_state`, then stores the computed state. When the computed state is lower (mileage corrected down, interval edited, marked done) it stores that lower state, or `NULL` for Upcoming, without notifying.
- **Rationale**: Gives exactly the behavior in the spec: Due soon then Overdue is two notifications, reloads and re-evaluations add none, a corrected mileage never produces a "new" alert, and a reminder that is marked done starts the next cycle clean. The update is a conditional `UPDATE ... WHERE notified_state IS DISTINCT FROM @Old` in the same transaction as the insert, so two evaluators running at once cannot both notify.
- **Alternatives considered**: Checking for an existing notification row (breaks once the user dismisses it and is not safe under concurrent runs); notifying on every evaluation (duplicates).

### D10. When the evaluator runs

- **Decision**: `ReminderEvaluator.EvaluateVehicleAsync(ownerId, vehicleId)` runs after reminder create, edit, complete, and resume, after a vehicle update, after service record create, update, and delete, and after an accepted draft creates a service record. `ReminderSweepService` runs `EvaluateAllAsync` hourly (`Reminders:Sweep:Enabled`, `IntervalMinutes`, default 60) to catch date-based changes and any write path not hooked. A failure in the evaluator is logged by type and never fails the write that triggered it.
- **Rationale**: Meets FR-015 (mileage-based notices when the mileage is saved; date-based within one day) with a short, auditable list of hook points. The sweep is the backstop for the rest.
- **Alternatives considered**: A database trigger (hides logic outside the C# test suite and violates the SQL-in-files convention for behavior); evaluating on every page request (slow and a hidden side effect of a read); a sweep only (mileage notices up to an hour late).

### D11. Today's date

- **Decision**: Today is `DateOnly.FromDateTime(TimeProvider.GetUtcNow().UtcDateTime)`. All dates in this feature are date-only with no time zone.
- **Rationale**: The app stores no user time zone, and the spec states date-only values carry none. `TimeProvider` makes tests deterministic.
- **Risk**: For a user far from UTC the due date can flip up to a day early or late in the local evening or morning. Accepted for this version; a per-user time zone is a later change.

### D12. Endpoints and shapes

- **Decision**: Vehicle-scoped group `/api/vehicles/{vehicleId}/reminders` (list, create, get with history, update, delete, complete, pause, resume), a garage-wide `GET /api/reminders` (optional `status` filter), and `GET /api/dashboard`. List responses are plain arrays ordered most urgent first (Overdue, Due soon, Mileage needed, Upcoming, Paused; then by earliest due date, then fewest miles left, then name), not paged.
- **Rationale**: A vehicle has a handful of reminders; the spec asks only that the list stay usable, and paging would hide the most urgent ones on page two. The urgency order is applied in C# after the calculator, because status is derived.
- **Alternatives considered**: Paging with a status sort in SQL (not possible without the derived status); a per-vehicle cap (adds a rule nobody asked for; revisit if real data needs it).

### D13. Dashboard

- **Decision**: `GET /api/dashboard` returns counts from one `Dashboard/Counts.sql` row (vehicles, service records, fuel and charging entries, drafts awaiting review, all scoped to the owner), the overdue and due-soon reminder counts from the same calculator, and up to five attention reminders. The Web page `/dashboard` shows metric cards linking to `/vehicles`, `/drafts`, and `/reminders` where those pages exist, an attention list, and an empty-garage prompt. It is added to the main navigation. The existing home page is unchanged.
- **Rationale**: Only data that exists today (planning input). New metrics later add fields to `DashboardDto` and cards to the page without touching existing ones; unknown fields are ignored by the Web client, so versions can deploy in either order.
- **Alternatives considered**: Replacing the home page (changes a page other specs assert on; the dashboard can be promoted later); one endpoint per metric (more round trips for a page that always shows them together).

### D14. Web structure

- **Decision**: A `reminders` tab (id `reminders`, label Reminders, a bell or alarm icon) is added to `VehicleTabs.All`, excluded from the edit form like Maintenance, with a pane in `VehicleDetail.razor` that renders `ReminderTab` (list with status badges and an Add reminder button). Add, edit, and detail pages live under `/vehicles/{id}/reminders/...`. The detail page shows next due, history, the mark-done form, pause or resume, and a confirm-to-delete panel using the existing `details` pattern. The delete-vehicle confirmation text gains "reminders". Same-name reminders on a vehicle are allowed; the form shows a non-blocking warning when the name already exists.
- **Rationale**: Mirrors the registrations and maintenance pages so conformance tests and users find familiar behavior; no JavaScript.
- **Alternatives considered**: Inline mark-done in the list (needs interactivity); a modal (not available in static SSR without script).

### D15. Logging and privacy

- **Decision**: Logs and metrics name only ids, counts, and states. Reminder names, subjects, and mileage never appear in logs (FR-021). Notification titles carry the reminder name and vehicle name because the user sees them, and are truncated to the column limit of 120 characters.

## Resolved clarifications

None required. Every `NEEDS CLARIFICATION` item in the Technical Context is filled in.
