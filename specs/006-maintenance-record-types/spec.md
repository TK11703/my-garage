# Feature Specification: Charging and Fueling, Inspections, Registrations, Insurance, and Taxes

**Feature Branch**: `006-maintenance-record-types`

**Created**: 2026-11-02

**Status**: Draft

**Input**: User description: "We're going to enhance the maintenance recording capabilities we have. We're going to add inspections, charging, fueling, and taxes records now."

**Refinement (2026-11-02)**: The vehicle's Maintenance tab stays and holds three sections: "Service Records" (complete), "Charging and Fueling", and "Inspections, Registrations, Insurance, and Taxes". A Charging and Fueling entry is either fuel (price per gallon, total gallons, total) or energy for an electric vehicle (price per kWh, kWh delivered, total session cost). An inspection tracks Type (Safety or Emissions), Date, Cost, and Next Due Date. A registration tracks State, Cost, and Expiration Date. A tax tracks Type (Personal Property, Lease, Purchase), Amount, and Recurrence (Once, Monthly, Bi-Annual, Annual).

**Refinement (2026-11-02, insurance)**: Insurance is tracked too, as its own list in the same section. An insurance entry holds only a Start Date, an End Date, a Duration, and a Cost.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Record fuel and charging entries (Priority: P1)

A signed-in user opens a vehicle's Maintenance tab and the "Charging and Fueling" section, which lists every entry kept for that vehicle, newest first. The section offers two actions: add a fuel entry and add a charging entry. A fuel entry captures the date, the price per gallon, the total gallons, and the total. A charging entry captures the date, the price per kWh, the kWh delivered, and the total session cost. If the user leaves the total empty, it is calculated from the price and the amount. A vehicle with no entries shows a friendly empty state that invites the user to add the first one.

**Why this priority**: Fueling and charging are the most frequent things a vehicle owner records, and a running log of what each fill-up or session cost is useful on its own.

**Independent Test**: Open a vehicle with no entries and confirm the empty state; add a fuel entry and a charging entry with valid values and confirm both appear with the values entered; add an entry with the total left empty and confirm the calculated total; confirm the newer entry stays first.

**Acceptance Scenarios**:

1. **Given** a signed-in user viewing a vehicle with no fuel or charging entries, **When** they open the Charging and Fueling section, **Then** they see an empty state with clear actions to add a fuel entry or a charging entry.
2. **Given** a user adding a fuel entry with a date, price per gallon, total gallons, and total, **When** they save, **Then** the entry is stored against that vehicle and listed showing those values.
3. **Given** a user adding a charging entry with a date, price per kWh, kWh delivered, and total session cost, **When** they save, **Then** the entry is stored against that vehicle and listed showing those values.
4. **Given** a user who leaves the total empty, **When** they save, **Then** the total shown is the price multiplied by the amount, rounded to the nearest cent.
5. **Given** a user who enters a total that differs from price multiplied by amount (for example a session fee, or pump rounding), **When** they save, **Then** the total entered is kept as the entry's total.
6. **Given** a user who leaves a required value empty or enters an invalid one (a negative price, an amount of zero, a date in the future), **When** they save, **Then** nothing is stored and each problem is identified with a specific message next to its field.
7. **Given** a vehicle with many entries, **When** the user views the section, **Then** entries are listed newest first, a page at a time, with a way to reach the rest.

---

### User Story 2 - Record inspections (Priority: P2)

In the "Inspections, Registrations, Insurance, and Taxes" section, the user sees an Inspections list and can add an inspection. An inspection has a Type (Safety or Emissions), a Date, a Cost, and a Next Due Date. The list shows the newest inspection first.

**Why this priority**: Inspections have deadlines and costs worth keeping, but they happen far less often than fueling.

**Independent Test**: Add a safety inspection with a next due date and an emissions inspection without one; confirm both are listed with the values entered; confirm a next due date earlier than the inspection date is rejected.

**Acceptance Scenarios**:

1. **Given** a vehicle with no inspections, **When** the user views the Inspections list, **Then** they see an empty state with a clear action to add an inspection.
2. **Given** a user who selects a type, enters a date and a cost, and saves, **Then** the inspection is stored against that vehicle and listed with the values entered, and a missing Next Due Date is shown as "Not set".
3. **Given** a user who enters a Next Due Date, **When** they save, **Then** it is shown with the inspection.
4. **Given** a user who enters a Next Due Date earlier than the inspection date, a date in the future for the inspection date, or an invalid cost, **When** they save, **Then** nothing is stored and each problem is identified next to its field.
5. **Given** a vehicle with several inspections, **When** the user views the list, **Then** the newest inspection date is first, a page at a time.

---

### User Story 3 - Record registrations (Priority: P2)

The user sees a Registrations list in the same section and can add a registration with the State, the Cost, and the Expiration Date. The list shows the registration that expires latest first.

**Why this priority**: Registration renewals are a recurring cost and a deadline, but are infrequent.

**Independent Test**: Add two registrations for different states and expiration dates; confirm both are listed with the values entered and that the later expiration is first.

**Acceptance Scenarios**:

1. **Given** a vehicle with no registrations, **When** the user views the Registrations list, **Then** they see an empty state with a clear action to add a registration.
2. **Given** a user who chooses a state, enters a cost, and enters an expiration date, **When** they save, **Then** the registration is stored against that vehicle and listed with the values entered.
3. **Given** a user who leaves a required value empty or enters an invalid cost or date, **When** they save, **Then** nothing is stored and each problem is identified next to its field.
4. **Given** a registration whose expiration date has already passed (for example a past renewal), **When** the user saves it, **Then** it is accepted, since past registrations are part of the history.

---

### User Story 4 - Record taxes (Priority: P2)

The user sees a Taxes list in the same section and can add a tax with a Type (Personal Property, Lease, or Purchase), an Amount, and a Recurrence (Once, Monthly, Bi-Annual, or Annual). The list shows the most recently added tax first.

**Why this priority**: Taxes complete the picture of what a vehicle costs, but there are few of them per vehicle.

**Independent Test**: Add a one-time purchase tax and an annual personal property tax; confirm both are listed with the values entered.

**Acceptance Scenarios**:

1. **Given** a vehicle with no taxes, **When** the user views the Taxes list, **Then** they see an empty state with a clear action to add a tax.
2. **Given** a user who chooses a type and a recurrence and enters an amount, **When** they save, **Then** the tax is stored against that vehicle and listed with the values entered.
3. **Given** a user who leaves a required value empty or enters an invalid amount, **When** they save, **Then** nothing is stored and each problem is identified next to its field.
4. **Given** a vehicle with many taxes, **When** the user views the list, **Then** it is shown a page at a time.

---

### User Story 8 - Record insurance (Priority: P2)

The user sees an Insurance list in the same section and can add an insurance entry with a Start Date, an End Date, a Duration (how often the premium is paid: Monthly, Bi-Annual, or Annual), and a Cost. The cost is the premium paid each time, at that frequency. The list shows the entry that ends latest first, so the current coverage is at the top.

**Why this priority**: Insurance is one of the largest ongoing costs of a vehicle and its end date is worth keeping, but there are few entries per vehicle.

**Independent Test**: Add a Bi-Annual entry and an Annual entry with different end dates; confirm both are listed with the values entered and that the later end date is first.

**Acceptance Scenarios**:

1. **Given** a vehicle with no insurance entries, **When** the user views the Insurance list, **Then** they see an empty state with a clear action to add insurance.
2. **Given** a user who enters a start date, an end date, a duration (how often the premium is paid), and a cost, **When** they save, **Then** the entry is stored against that vehicle and listed with the values entered.
3. **Given** a user who leaves a required value empty (including the duration), enters an end date that is not after the start date, or enters an invalid cost (for example a negative one), **When** they save, **Then** nothing is stored and each problem is identified next to its field.
4. **Given** an insurance entry whose end date has already passed, **When** the user saves it, **Then** it is accepted, since past coverage is part of the history.
5. **Given** a vehicle with many insurance entries, **When** the user views the list, **Then** it is shown a page at a time.

---

### User Story 5 - Correct and remove entries (Priority: P1)

A user can edit any fuel or charging entry, inspection, registration, insurance entry, or tax on their vehicle to correct a mistake, and can delete one that was entered in error. Deleting asks for confirmation first, and the user can cancel. After saving or deleting, the user returns to the Maintenance tab at the section they were working in.

**Why this priority**: Entries are typed by hand and will contain mistakes; without correction and removal the history cannot be trusted.

**Independent Test**: For each of the five kinds, add an entry, edit a value and confirm the change is shown, then delete it after cancelling once and confirm it is gone.

**Acceptance Scenarios**:

1. **Given** an entry of any kind, **When** the user chooses to edit it, **Then** the form is filled with its current values.
2. **Given** a user editing an entry, **When** they change values and save, **Then** the changes are stored and shown, and the entry keeps its place in the list according to its date.
3. **Given** a user editing an entry, **When** they enter an invalid value and save, **Then** nothing is changed and each problem is identified next to its field.
4. **Given** a user who chooses to delete an entry and cancels the confirmation, **Then** the entry is kept.
5. **Given** a user who chooses to delete an entry and confirms, **Then** the entry is removed and the list no longer shows it.

---

### User Story 6 - Entries stay with their vehicle and stay private (Priority: P1)

Fuel and charging entries, inspections, registrations, insurance entries, and taxes are visible and changeable only by the owner of the vehicle they belong to. Another signed-in user cannot see, add, change, or delete them, even by guessing an address or identifier. An entry reached through a vehicle other than its own behaves as not found. Deleting a vehicle deletes all of its entries.

**Why this priority**: Vehicle history and spending are personal data; access control must hold from the first release.

**Independent Test**: With two users, confirm neither can list, open, add, edit, or delete the other's entries of any kind, including by using an entry's identifier directly, and that deleting a vehicle removes its entries.

**Acceptance Scenarios**:

1. **Given** two signed-in users, **When** one tries to list or add entries of any kind on the other's vehicle, **Then** the application responds as if the vehicle does not exist.
2. **Given** two signed-in users, **When** one tries to open, edit, or delete the other's entry by its identifier, **Then** the application responds as if the entry does not exist and nothing is changed.
3. **Given** an entry on one of the user's vehicles, **When** the user tries to open, edit, or delete it through a different vehicle of theirs, **Then** the application responds as if the entry does not exist.
4. **Given** a signed-out visitor, **When** they request any page of this feature, **Then** they are asked to sign in and are returned to the page afterwards.
5. **Given** a vehicle with entries of every kind, **When** the user deletes the vehicle, **Then** all of those entries are deleted and none remain.
6. **Given** a user saving or viewing entries, **When** logs and page addresses are inspected, **Then** no entry contents appear in them.

---

### User Story 7 - Find each list at a glance on the Maintenance tab (Priority: P2)

The Maintenance tab shows three collapsible sections: Service Records, Charging and Fueling, and Inspections, Registrations, Insurance, and Taxes. Each section's heading shows how many entries it holds and, where there is a date, the most recent one, so users can tell where they stand without opening it. After saving, editing, or deleting an entry, the section the user was working in is open.

**Why this priority**: It organizes the tab so five more kinds of record stay easy to find, but the lists work without it.

**Independent Test**: With entries in every section, confirm each heading shows the correct count and latest date; add an entry and confirm the heading updates and its section is open on return.

**Acceptance Scenarios**:

1. **Given** a vehicle with entries, **When** the user views the Maintenance tab, **Then** each section heading shows its entry count and, for Charging and Fueling, the date of the most recent entry; the combined section shows a count for each of its four lists.
2. **Given** the most recent entry is deleted, **When** the user returns to the tab, **Then** the counts and dates reflect the remaining entries.
3. **Given** a user who has just saved or deleted an entry, **When** they return to the Maintenance tab, **Then** the section holding that entry is open.
4. **Given** the existing Service Records section, **When** the user uses it, **Then** it behaves exactly as before.

---

### Edge Cases

- A user saves with only the required values: it is saved and optional values (a missing Next Due Date) are shown as "Not set".
- A price or amount has more decimal places than allowed: it is rejected with a message that says how many are allowed.
- A free charging session or a free inspection (cost zero) is accepted.
- A date that describes something that already happened (fuel or charge date, inspection date) is in the future: it is rejected. A Next Due Date or an Expiration Date may be in the future or the past.
- Two entries of the same kind share the same date: both are kept and listed in a stable order, most recently added first.
- A user opens a vehicle or entry that no longer exists or never existed: behaves as not found.
- A user opens an entry that was deleted in another browser tab: behaves as not found, with a message and a way back to the Maintenance tab.
- A user edits an entry while the session expires: the user is prompted to sign in again, nothing is half-saved, and no other user's data is shown.
- A vehicle has a very large number of entries of one kind: the list is paged and remains usable on a narrow phone screen without horizontal scrolling of the page.
- A user records a fuel entry for an electric vehicle or a charging entry for a gasoline vehicle: it is accepted; the application does not restrict entries by the vehicle's fuel type.
- A registration, insurance entry, or tax with the same values as an existing one is entered: both are kept; duplicates are the user's to remove.
- An insurance entry's dates and duration are not checked against other insurance entries, and the duration is not checked against the span between the start and end dates; overlapping or gapped coverage is accepted.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each fuel or charging entry, inspection, registration, insurance entry, and tax MUST belong to exactly one vehicle, and each vehicle MUST have its own independent collection of each.
- **FR-002**: The Maintenance tab MUST present three sections, in this order: Service Records (unchanged), Charging and Fueling, and Inspections, Registrations, Insurance, and Taxes, with the lists of inspections, registrations, insurance, and taxes shown together in the third.
- **FR-003**: Users MUST be able to add, list, edit, and delete entries of each of the five kinds on any of their own vehicles, with a confirmation step before a delete.
- **FR-004**: Lists MUST be ordered newest first (fuel and charging by date; inspections by date; registrations by expiration date; insurance by end date; taxes by when they were added) and paged, with entries beyond the first page reachable.
- **FR-005**: A fuel entry MUST capture a date, a price per gallon, the total gallons, and a total. A charging entry MUST capture a date, a price per kWh, the kWh delivered, and the total session cost. When the total is left empty it MUST be calculated as the price multiplied by the amount, rounded to the nearest cent; a total entered by the user MUST be kept as entered.
- **FR-006**: An inspection MUST capture a Type (Safety or Emissions), a Date, and a Cost, and MAY capture a Next Due Date that is not earlier than the inspection date.
- **FR-007**: A registration MUST capture a State, a Cost, and an Expiration Date.
- **FR-008**: A tax MUST capture a Type (Personal Property, Lease, or Purchase), an Amount, and a Recurrence (Once, Monthly, Bi-Annual, or Annual).
- **FR-009**: The system MUST validate every entry on add and edit before storing it, MUST store nothing when validation fails, and MUST identify each problem with a specific message next to its field. The implementation plan's data model is the authoritative definition of ranges and limits.
- **FR-010**: The system MUST verify server-side, for every operation on every kind of entry, including access by entry identifier, that the entry's parent vehicle belongs to the signed-in user; an entry or vehicle that is not the user's MUST behave as not found.
- **FR-011**: The system MUST derive the owner from the signed-in user's validated identity and MUST NOT accept any user or owner identifier from the request as a basis for access.
- **FR-012**: The system MUST require sign-in for every page and operation of this feature; signed-out visitors MUST be asked to sign in and returned to the page afterwards.
- **FR-013**: The system MUST delete all of a vehicle's entries of these five kinds when the vehicle is deleted.
- **FR-014**: The system MUST record who created and last changed each entry and when, attributable to the authenticated user.
- **FR-015**: Each section heading MUST show the number of entries in it and, for Charging and Fueling, the date of the most recent entry; each list shows an empty state when it has no entries.
- **FR-016**: After adding, editing, or deleting an entry, the user MUST return to the Maintenance tab with the section that holds that entry open.
- **FR-017**: The system MUST NOT write entry contents to logs or place them in page addresses.
- **FR-018**: Entry values MUST always be displayed as plain text so that user-entered content cannot alter the page.
- **FR-019**: The new sections and their add and edit screens MUST follow the application's UI standard and remain usable, accessible, and keyboard-operable on narrow phone screens.
- **FR-020**: Existing vehicles, service records, and their data MUST continue to work unchanged; vehicles with none of the new entries MUST display normally.
- **FR-021**: An insurance entry MUST capture a Start Date, an End Date that is later than the start date, a Duration (how often the premium is paid: Monthly, Bi-Annual, or Annual), and a Cost (the premium paid each time), and nothing else.

### Key Entities

- **Fuel or Charging Entry**: One fill-up or charging session: kind (fuel or energy), date, price per unit (per gallon or per kWh), amount (gallons or kWh), and total cost. Belongs to exactly one vehicle.
- **Inspection**: One inspection: type (Safety or Emissions), date, cost, and an optional next due date. Belongs to exactly one vehicle.
- **Registration**: One registration: state, cost, and expiration date. Belongs to exactly one vehicle.
- **Insurance**: One period of insurance coverage: start date, end date, duration (how often the premium is paid: Monthly, Bi-Annual, or Annual), and cost (the premium paid each time). Belongs to exactly one vehicle.
- **Tax**: One tax: type (Personal Property, Lease, Purchase), amount, and recurrence (Once, Monthly, Bi-Annual, Annual). Belongs to exactly one vehicle.
- **Vehicle**: An existing entity owned by one user. It gains five collections, one for each kind above.

Each kind also records who created and last changed it and when, and cannot exist without its vehicle.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can add a complete fuel or charging entry in under 1 minute from opening the vehicle's Maintenance tab.
- **SC-002**: A user can add a complete inspection, registration, insurance entry, or tax in under 1 minute from opening the vehicle's Maintenance tab.
- **SC-003**: 90% of users can add, correct, and delete an entry of any of the five kinds on their first attempt without help.
- **SC-004**: A vehicle with 500 entries of one kind still shows the first page of that list in under 2 seconds.
- **SC-005**: In testing with two users, 100% of attempts to list, view, add, change, or delete another user's entries of any kind are refused as not found, and none of the other user's data is revealed.
- **SC-006**: 100% of invalid submissions are rejected with a message beside each offending field and leave stored data unchanged.
- **SC-007**: Deleting a vehicle leaves zero fuel or charging entries, inspections, registrations, insurance entries, or taxes behind for that vehicle.
- **SC-008**: All existing vehicle and service record features continue to work with no change in behavior for vehicles that have none of the new entries.

## Assumptions

- The exact ranges, limits, and choice lists (including the list of states) are defined in the implementation plan's data model; this specification defines the behavior around those fields.
- Fuel is measured in gallons and energy in kWh; amounts are in a single currency. Other units and per-user unit preferences are out of scope.
- A fuel or charging entry has a date, even though the request did not list one: without it entries cannot be ordered or tied to when they happened.
- A tax has no date of its own; taxes are ordered by when they were added. A date can be added in a later feature if wanted.
- "Bi-Annual" means twice a year.
- Insurance is its own list with only a Start Date, an End Date, a Duration, and a Cost, as requested; it has no insurer, policy number, or coverage details. The Duration is how often the premium is paid: Monthly, Bi-Annual (twice a year), or Annual. The Cost is the premium paid each time, not the total for the policy period. The Duration is chosen by the user and is not derived from the dates, and no total is calculated.
- Fuel and charging entries do not capture the odometer reading, and they do not change the mileage shown on the My Vehicles card.
- The State on a registration is chosen from the 50 United States and the District of Columbia.
- Fuel economy, spending totals, reminders for due dates and expirations, reports, search, filtering, exporting, and attachments such as receipts are out of scope.
- Hard deletion is acceptable; there is no undo or recycle bin.
- Users, vehicles, sign-in, the Maintenance tab, service records, and the UI standard already exist and are reused as they are.
- The feature is used on desktop and phone browsers, with a stable internet connection.
