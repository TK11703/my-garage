# Feature Specification: Service Reminders

**Feature Branch**: `013-service-reminders`

**Created**: 2026-11-06

**Status**: Draft

**Input**: User description: "I need to be able to set up reminders. Things like oil changes or other services that occur every X miles. Inspections every X year(s). Registrations every X year(s)."

**Refinement (2026-11-06, planning input)**: Reminders always belong to a vehicle, because what triggers a reminder may be the vehicle itself, equipment on the vehicle, or the location where the vehicle resides, so each reminder says which of the three it applies to. Reminders live in a new Reminders tab on the vehicle details page. A garage dashboard is added as well; it shows the metrics My Garage already holds (vehicles, service records, reminders due, and so on) and more metrics, such as fuel usage graphs, will be added in later iterations.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Set up a recurring reminder for a vehicle (Priority: P1)

A signed-in user opens one of their vehicles and sets up a reminder for something that recurs: an oil change every 5,000 miles, a state inspection every year, a registration renewal every 2 years. They give the reminder a name (suggested names such as Oil change, Tire rotation, Inspection, and Registration are offered, and any custom name is allowed), choose how often it recurs by distance, by time, or by both (for example "every 5,000 miles or 6 months, whichever comes first"), and say when it was last done (date and mileage), defaulting to today and the vehicle's current mileage. My Garage works out when the item is next due and shows it with the reminder.

**Why this priority**: This is the core of the feature. Without recurring reminders that know their next due point, nothing else has value.

**Independent Test**: Open a vehicle with a known mileage, add an oil-change reminder of every 5,000 miles last done at 40,000 miles, and an inspection reminder of every 1 year last done on a given date; confirm each shows the correct next due mileage or date and appears in the vehicle's reminder list.

**Acceptance Scenarios**:

1. **Given** a vehicle the user owns, **When** they add a reminder named "Oil change" that recurs every 5,000 miles and was last done at 40,000 miles, **Then** the reminder is saved and shows next due at 45,000 miles.
2. **Given** a vehicle the user owns, **When** they add a reminder named "Inspection" that recurs every 1 year and was last done on 2026-03-15, **Then** the reminder is saved and shows next due on 2027-03-15.
3. **Given** a reminder with both a distance interval and a time interval, **When** the user views it, **Then** it shows both next due points and treats the item as due when either is reached first.
4. **Given** the add-reminder form, **When** the user submits no name, no interval, a non-positive or non-whole interval, a last-done date in the future, or a negative last-done mileage, **Then** the reminder is not saved and each problem is identified clearly.
5. **Given** the add-reminder form for a vehicle with no recorded mileage, **When** the user adds a distance-based reminder, **Then** they are asked for the last-done mileage and are told that the reminder cannot show a status by distance until the vehicle has a mileage.
6. **Given** two users, **When** one sets up reminders, **Then** only that user can see or change them.

---

### User Story 2 - See what is upcoming, due soon, or overdue (Priority: P1)

A user can see at a glance which reminders need attention. Each reminder shows a status: Upcoming, Due soon, or Overdue, with how far away it is ("1,200 miles left", "due in 18 days", "400 miles overdue"). A vehicle's reminder list is sorted with the most urgent first. A garage-wide reminders view lists reminders across all the user's vehicles, with overdue and due-soon items first, so the user does not have to open each vehicle.

**Why this priority**: A reminder the user must go looking for is not a reminder. Showing urgency across the garage is what makes the list useful.

**Independent Test**: Create reminders on two vehicles in each state (one comfortably ahead, one close, one past due); confirm each shows the right status and remaining distance or time, the vehicle list and the garage-wide view order them most urgent first, and another user's reminders never appear.

**Acceptance Scenarios**:

1. **Given** a reminder whose next due point is far off, **When** the user views it, **Then** its status is Upcoming and it shows the distance or time remaining.
2. **Given** a reminder within 500 miles or 30 days of its next due point, **When** the user views it, **Then** its status is Due soon.
3. **Given** a reminder whose due mileage or date has been reached or passed, **When** the user views it, **Then** its status is Overdue and it shows how far past due it is.
4. **Given** a reminder with both a distance and a time interval, **When** the two next due points differ in urgency, **Then** the status and remaining amount are based on the more urgent of the two.
5. **Given** a user with reminders on several vehicles, **When** they open the garage-wide reminders view, **Then** they see all of them, overdue first, then due soon, then upcoming, each labeled with its vehicle, and can open the vehicle from the entry.
6. **Given** a user with no reminders, **When** they open the reminders view, **Then** they see a message explaining reminders and a way to add the first one.
7. **Given** a vehicle's mileage increases (for example a new service record with a higher odometer or an edit to the mileage), **When** the user next views its reminders, **Then** the distance-based statuses reflect the new mileage.

---

### User Story 3 - Mark a reminder as done and roll it forward (Priority: P1)

When the user has had the work done, they mark the reminder as done, confirming the date and mileage (defaulting to today and the vehicle's current mileage). The reminder resets from those values and shows its new next due point, so it keeps working for the next cycle. The previous completions stay available as a short history on the reminder.

**Why this priority**: Without this the reminder becomes overdue forever and the feature cannot be used past the first cycle.

**Independent Test**: Mark an overdue oil-change reminder done at 46,200 miles on a given date; confirm it becomes Upcoming with next due at 51,200 miles, the earlier due point appears in its history, and an inspection reminder marked done rolls to one year after the chosen date.

**Acceptance Scenarios**:

1. **Given** a reminder, **When** the user marks it done with a date and mileage, **Then** the last-done values are updated, the next due point is recalculated from them, and the status is recomputed.
2. **Given** the mark-as-done form, **When** the user enters a future date, a mileage lower than the reminder's previous last-done mileage, or a negative mileage, **Then** nothing changes and the problem is identified clearly.
3. **Given** a reminder that has been marked done several times, **When** the user opens it, **Then** it lists its completions (date and mileage), most recent first.
4. **Given** a time-only reminder, **When** the user marks it done, **Then** the mileage is optional and the next due date is recalculated from the date done.
5. **Given** a mileage entered when marking done that is higher than the vehicle's recorded mileage, **When** the user saves, **Then** the vehicle's mileage is offered an update to that value and is changed only if the user agrees.

---

### User Story 4 - Be notified when a reminder becomes due (Priority: P2)

The user does not need to open the app to find out an item is due. When a reminder reaches Due soon, and again when it reaches Overdue, the user gets a notification through the notifications already shown in the navigation bar, naming the reminder and vehicle, with a link to it. Each state change notifies once; opening the app repeatedly does not create duplicates. Marking the reminder done clears any outstanding notification for it.

**Why this priority**: Notification is what turns a list into a reminder, but the list and the status in Stories 1 to 3 already deliver value on their own.

**Independent Test**: Create a reminder whose due date is within 30 days and one that is already overdue; confirm one notification appears for each, reloading the app creates no repeats, and marking one done removes its notification.

**Acceptance Scenarios**:

1. **Given** a reminder that newly reaches Due soon by date, **When** the date threshold is crossed, **Then** the user receives one notification within one day, naming the reminder and vehicle.
2. **Given** a reminder that newly reaches Due soon or Overdue by mileage because the vehicle's mileage increased, **When** the mileage is saved, **Then** the user receives one notification at that time.
3. **Given** a reminder that moves from Due soon to Overdue, **When** the change occurs, **Then** the user receives a second notification for the new state and not a repeat of the first.
4. **Given** a notification for a reminder, **When** the user opens it, **Then** they go to that vehicle's reminder.
5. **Given** a reminder with an outstanding notification, **When** the user marks it done, **Then** the notification no longer appears as needing attention.
6. **Given** two users, **When** a reminder of one becomes due, **Then** only that user is notified.

---

### User Story 5 - Edit, pause, and delete reminders (Priority: P2)

A user can change a reminder's name or intervals, correct its last-done values, pause it (for example while a vehicle is in storage) so it shows no status and sends no notifications, resume it, or delete it after confirming. Deleting a vehicle removes its reminders.

**Why this priority**: Reminders need upkeep as real life changes, but the core flow works without it.

**Independent Test**: Edit an interval and confirm the next due point recalculates; pause a reminder and confirm it shows Paused, is excluded from statuses and notifications; resume it and confirm it returns; delete it after confirmation; delete a vehicle and confirm its reminders are gone.

**Acceptance Scenarios**:

1. **Given** a reminder, **When** the user changes its interval or last-done values and saves, **Then** the next due point and status are recalculated.
2. **Given** a reminder, **When** the user pauses it, **Then** it shows as Paused, is excluded from the due-soon and overdue lists and from notifications, and keeps its settings and history.
3. **Given** a paused reminder, **When** the user resumes it, **Then** its status is recomputed from the current mileage and date and any newly due state is notified.
4. **Given** a reminder, **When** the user chooses to delete it and confirms, **Then** it and its history are removed; if they cancel, nothing changes.
5. **Given** a vehicle with reminders, **When** the user deletes the vehicle, **Then** its reminders, history, and notifications are removed with it.

---

### User Story 6 - See the garage at a glance on a dashboard (Priority: P2)

The user opens a garage dashboard from the main navigation. It summarizes the whole garage with the figures My Garage already holds: how many vehicles they own, how many service records and fuel or charging entries they have logged, how many reminders are overdue and due soon, and how many maintenance drafts await review. Below the figures it lists the reminders that need attention first, each linking to its vehicle. The dashboard shows only information that exists today; further metrics such as fuel usage graphs are added in later iterations without changing what is already there.

**Why this priority**: It gives reminders a home beyond the individual vehicle and a place to grow, but reminders deliver value without it.

**Independent Test**: With two vehicles, several service records, and reminders in each state, open the dashboard and confirm each figure matches the garage, the attention list shows the most urgent reminders first, and a user with an empty garage sees zeros and an invitation to add a vehicle.

**Acceptance Scenarios**:

1. **Given** a signed-in user, **When** they open the dashboard, **Then** they see their count of vehicles, service records, fuel and charging entries, overdue reminders, due-soon reminders, and drafts awaiting review, and nothing from any other user.
2. **Given** reminders in several states, **When** the user opens the dashboard, **Then** up to five reminders needing attention are listed, overdue first, each naming its vehicle and linking to it.
3. **Given** a user with no vehicles, **When** they open the dashboard, **Then** every figure shows zero and the page invites them to add their first vehicle.
4. **Given** a figure on the dashboard, **When** the user selects it, **Then** they go to the page that lists those items (vehicles, drafts, or reminders) where one exists.
5. **Given** the main navigation, **When** the user looks for it, **Then** the dashboard is reachable from there.

---

### Edge Cases

- A reminder that applies to equipment or a location rather than the vehicle itself: the name of that equipment or location is shown with the reminder and in notifications so the user can tell which one is due.
- A vehicle with no recorded mileage and only distance-based reminders: reminders show "Mileage needed" instead of a status until a mileage is recorded; time-based parts work normally.
- A vehicle's mileage decreases because of a correction: statuses are recomputed from the new value; no new notifications are sent for states that move to a less urgent one.
- A reminder is marked done early, before it is due: the next due point is still computed from the actual date and mileage entered.
- A last-done date that is long ago, so the first calculated due date is already in the past: the reminder is Overdue when saved, and the user is notified once.
- A due date that falls on a day absent from the target month (for example a 1-year interval from 29 February): the due date is the last day of that month.
- Duplicate reminder names on one vehicle: allowed, since some vehicles carry two similar items, but the form warns when the same name already exists.
- A vehicle has many reminders: the list stays usable, paged or scrollable, with the most urgent first.
- The user edits a reminder while its notification is outstanding: the notification stays or clears based on the recalculated status.
- Odometer readings that stay constant for a long time with a time-and-distance reminder: the time-based part still triggers on its own.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to create, view, edit, pause, resume, and delete reminders on vehicles they own, and only their own; a reminder belongs to exactly one vehicle.
- **FR-002**: A reminder MUST have a name (required, up to 100 characters), an applies-to value, a recurrence interval, and last-done values. The form MUST offer suggested reminder types (Oil change, Tire rotation, Inspection, Registration) that fill in the name and a typical interval the user can change, and MUST allow any custom name.
- **FR-002a**: A reminder MUST say whether it applies to the vehicle itself, to equipment on the vehicle, or to the location where the vehicle resides. For equipment and location the user MUST name which one (up to 100 characters, for example "Trailer hitch" or "Home garage permit"); that name MUST be shown with the reminder and in its notifications. Suggested types apply to the vehicle.
- **FR-003**: A reminder's recurrence MUST be by distance (whole miles, 1 to 999,999), by time (whole months or whole years, with years allowed from 1 to 50 and months from 1 to 600), or both. At least one MUST be provided.
- **FR-004**: A reminder MUST record when it was last done: a date (not in the future) and, for distance-based reminders, a mileage (whole number 0 to 9,999,999). The form MUST default these to today and the vehicle's current mileage.
- **FR-005**: The system MUST calculate the next due mileage as the last-done mileage plus the distance interval and the next due date as the last-done date plus the time interval, and MUST treat a reminder with both as due when either is reached first.
- **FR-006**: The system MUST assign each active reminder a status: Overdue when a due point has been reached or passed, Due soon when within 500 miles or 30 days of a due point, and Upcoming otherwise, based on the vehicle's current mileage and today's date.
- **FR-007**: A reminder MUST display its next due point(s) and the distance or time remaining or overdue, and for a reminder with both intervals MUST base the status on the more urgent.
- **FR-008**: The vehicle's current mileage for status purposes MUST be the same mileage shown for the vehicle in My Garage (the greater of its recorded mileage and its latest service record odometer).
- **FR-009**: The vehicle detail page MUST have a Reminders tab that shows the vehicle's reminders, most urgent first, with a way to add one.
- **FR-010**: The user MUST have a garage-wide reminders view listing reminders from all of their vehicles, grouped or ordered Overdue, then Due soon, then Upcoming, then Paused, each labeled with its vehicle and linking to it.
- **FR-011**: Users MUST be able to mark a reminder done with a date and mileage; doing so MUST update the last-done values, recalculate the next due point, and add an entry to the reminder's history. A completion date MUST NOT be in the future, and a mileage MUST NOT be below the reminder's previous last-done mileage.
- **FR-012**: Each reminder MUST keep a history of its completions (date and mileage), viewable most recent first.
- **FR-013**: When marking done with a mileage above the vehicle's recorded mileage, the system MUST offer to update the vehicle's mileage and MUST NOT change it without the user's agreement.
- **FR-014**: The system MUST send an in-app notification, through the existing navigation-bar notifications, when an active reminder newly reaches Due soon and again when it newly reaches Overdue. Each state change MUST notify once only, and the notification MUST name the reminder and vehicle and link to it.
- **FR-015**: Date-based state changes MUST produce their notification within one day of the threshold being crossed, and mileage-based changes MUST produce theirs when the vehicle's mileage is saved.
- **FR-016**: Marking a reminder done, pausing it, or deleting it MUST clear its outstanding notifications from needing attention.
- **FR-017**: Paused reminders MUST show a Paused status, MUST be excluded from due-soon and overdue lists and from notifications, and MUST keep their settings and history; resuming MUST recompute the status.
- **FR-018**: Deleting a reminder MUST require confirmation and MUST remove its history; deleting a vehicle MUST remove its reminders, history, and related notifications.
- **FR-019**: Reminders and notifications about them MUST be visible and actionable only by the vehicle's owner; any attempt to access another user's reminder MUST behave as not found.
- **FR-020**: Add, edit, and mark-done forms MUST validate input and identify each problem clearly without losing what the user entered.
- **FR-021**: Reminder details MUST NOT be disclosed in logs, and every change MUST be attributable to the signed-in user.
- **FR-022**: The system MUST provide a garage dashboard, reachable from the main navigation, showing the signed-in user's count of vehicles, service records, fuel and charging entries, overdue reminders, due-soon reminders, and drafts awaiting review, plus up to five reminders needing attention (overdue first), each linking to its vehicle.
- **FR-023**: The dashboard MUST show only information the system already holds and MUST handle an empty garage by showing zeros and an invitation to add a vehicle. Its figures MUST be scoped to the signed-in user.

### Key Entities

- **Reminder**: A recurring maintenance or compliance item on one vehicle. Has a name, what it applies to (the vehicle, equipment on it, or its location, with the equipment or location name), a distance interval and/or a time interval, last-done date and mileage, an active or paused state, and a derived next due mileage, next due date, and status.
- **Reminder completion**: One entry in a reminder's history, recording the date and mileage at which the item was done.
- **Reminder status**: A derived label (Upcoming, Due soon, Overdue, Paused, or Mileage needed) computed from the reminder, the vehicle's current mileage, and today's date; it is not set by the user.
- **Vehicle**: An existing entity owned by one user; supplies the current mileage and owns its reminders.
- **Notification**: An existing in-app alert shown in the navigation bar; gains reminder-due alerts that link to the reminder.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can set up a reminder, including its name, interval, and last-done values, in under 1 minute.
- **SC-002**: A user can mark a reminder done and see its new next due point in under 30 seconds.
- **SC-003**: A user with vehicles in several states can find every overdue and due-soon item in the garage from a single view without opening any vehicle.
- **SC-004**: 100% of reminders that reach Due soon or Overdue produce exactly one notification for that state, and none produce duplicates when the user reloads or signs in again.
- **SC-005**: Next due points and statuses match a manual calculation in 100% of tested combinations of distance-only, time-only, and combined intervals, including month-end dates.
- **SC-006**: No user can see, change, or be notified about another user's reminders in any tested scenario.
- **SC-007**: After a vehicle's mileage changes, the affected reminders show updated statuses on the next view without any other action by the user.
- **SC-008**: Every dashboard figure matches a manual count of the user's own data in 100% of tested garages, including an empty one, and the dashboard shows a user's overall garage state in a single view in under 10 seconds.

## Assumptions

- Distances are in miles, matching the mileage already used throughout My Garage; other units are out of scope.
- "Due soon" thresholds are fixed at 500 miles and 30 days for this version; making them adjustable is out of scope.
- Notifications are in-app only through the existing navigation-bar notifications; email, text message, and push alerts are out of scope.
- Marking a reminder done does not create a service record, and saving a service record does not complete a reminder; linking the two is out of scope and may follow as its own feature.
- The dashboard starts with the figures listed above; charts and other metrics (for example fuel usage graphs) are out of scope here and arrive in later features that add to the same dashboard.
- Reminders are created manually per vehicle; automatic suggestions from a vehicle's make, model, or service history, and sharing reminder templates across vehicles, are out of scope.
- Date-only values (last-done and due dates) have no time zone and are compared with the current calendar date.
- Existing vehicle, mileage, service record, and notification behavior is reused as is; this feature adds reminders and a new kind of notification.
- Users sign in with the existing authentication; no new roles or permissions are introduced.
