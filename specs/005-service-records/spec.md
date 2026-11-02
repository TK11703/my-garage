# Feature Specification: Vehicle Service Records

**Feature Branch**: `005-service-records`

**Created**: 2026-11-02

**Status**: Draft

**Input**: User description: "We need to start keeping track of service records. Each vehicle will have its own collection of service records. In the plan I will highlight the fields we need to collect."

**Refinement (2026-11-02)**: Each service record carries Date Serviced, Odometer Reading, and Shop Name or Person (open text). The user then adds line items identifying what service was performed, each with either a single cost or a breakdown of labor hours, parts used, and price per part; the total cost is shown from all line items. Records are listed on a "Service Records" tab of the vehicle details, most recent Date Serviced first, and can be edited and deleted. The mileage on the My Vehicles card comes from the latest service record or from a new vehicle field, "Latest Odometer Reading". The field definitions are in the plan's data model; see FR-018 to FR-021.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Add and view service records for a vehicle (Priority: P1)

A signed-in user opens one of their vehicles and sees a Service Records section listing every service record kept for that vehicle, newest service first. The user can add a new service record by filling in the service record fields and saving it. The new record then appears in that vehicle's list. A vehicle with no records shows a friendly empty state that invites the user to add the first one.

**Why this priority**: Recording service history is the reason service records exist; adding and seeing records is the minimum viable slice, and every other story builds on it.

**Independent Test**: Open a vehicle with no records and confirm the empty state; add a record with valid values and confirm it appears in the list with the values entered; add a second record with an earlier service date and confirm the newer service stays first.

**Acceptance Scenarios**:

1. **Given** a signed-in user viewing a vehicle with no service records, **When** they open the Service Records section, **Then** they see an empty state with a clear action to add a service record.
2. **Given** a user who enters valid values for every service record field, **When** they save, **Then** the record is stored against that vehicle and appears in its list showing the values entered.
3. **Given** a user who leaves a required field empty or enters an invalid value, **When** they save, **Then** nothing is stored and each problem is identified with a specific message next to its field.
4. **Given** a vehicle with several service records, **When** the user opens the Service Records section, **Then** the records are listed with the most recent service first.
5. **Given** a vehicle with many service records, **When** the user views the list, **Then** it stays usable by showing the records a page at a time, with a way to reach the rest.
6. **Given** a user with two vehicles, **When** they add a service record to one, **Then** it appears only on that vehicle and never on the other.

---

### User Story 2 - View, edit, and delete a service record (Priority: P1)

A user can open any service record on their vehicle to see its full details, correct mistakes by editing it, and remove it when it was entered in error. Deleting asks for confirmation first, and the user can cancel.

**Why this priority**: Records are entered by hand and will contain mistakes; without correction and removal the history cannot be trusted.

**Independent Test**: Add a record, open it and confirm every value is shown, edit a value and confirm the change is saved and shown, then delete it after confirming and verify it is gone from the list.

**Acceptance Scenarios**:

1. **Given** a service record on a vehicle, **When** the user opens it, **Then** every value saved with it is shown, and fields that were left empty are shown as "Not set".
2. **Given** a user editing a service record, **When** they change values and save, **Then** the changes are stored and shown, and the record keeps its place in the list according to its service date.
3. **Given** a user editing a service record, **When** they enter an invalid value and save, **Then** nothing is changed and each problem is identified next to its field.
4. **Given** a user who chooses to delete a service record, **When** they are asked to confirm and cancel, **Then** the record is kept.
5. **Given** a user who chooses to delete a service record, **When** they confirm, **Then** the record is removed and the list no longer shows it.

---

### User Story 3 - Itemize the work and see the total cost (Priority: P1)

On a service record, the user adds line items, each saying what service was performed. A line item is priced in one of two ways: a single cost, or a breakdown of the labor hours (at an hourly rate) and the parts used (name, quantity, and price per part). The record shows each line item's cost and a total cost that is the sum of all its line items. The user can edit and delete line items.

**Why this priority**: A record says when and where a vehicle was serviced; the line items say what was done and what it cost, which is the main value of keeping service history.

**Independent Test**: On a record, add one line item with a single cost and one with labor and parts, confirm each line's cost and the record total, then edit and delete line items and confirm the total updates.

**Acceptance Scenarios**:

1. **Given** a service record with no line items, **When** the user opens it, **Then** it shows a total cost of zero and an empty state inviting them to add what was done.
2. **Given** a user adding a line item with a description and a single cost, **When** they save, **Then** the line item appears with that cost and the record total includes it.
3. **Given** a user adding a line item with labor hours, an hourly rate, and one or more parts each with a quantity and price per part, **When** they save, **Then** the line item's cost is the labor cost plus the cost of every part, and the record total includes it.
4. **Given** a user entering both a single cost and a labor or parts breakdown on one line item, **When** they save, **Then** only the chosen pricing method is saved and the line item is never counted twice.
5. **Given** a line item with missing or invalid values (no description, labor hours without a rate, a part without a price, a quantity of zero), **When** the user saves, **Then** nothing is stored and each problem is identified next to its field.
6. **Given** a record with several line items, **When** the user edits a line item's pricing or deletes a line item, **Then** the record total is recalculated from the remaining line items.
7. **Given** a record that already has the maximum number of line items, **When** the user adds another, **Then** they are told the limit was reached and nothing is stored.

---

### User Story 4 - Service records stay with their vehicle and stay private (Priority: P1)

Service records are visible and changeable only by the owner of the vehicle they belong to. Another signed-in user cannot see, add, change, or delete records on a vehicle that is not theirs, even by guessing an address or identifier. When a vehicle is deleted, its service records are deleted with it.

**Why this priority**: Service history is personal data; access control must hold from the first release of the feature.

**Independent Test**: With two users, confirm that neither can list, open, add, edit, or delete the other's service records, including by using a record's identifier directly, and that deleting a vehicle removes its records.

**Acceptance Scenarios**:

1. **Given** two signed-in users, **When** one attempts to list or add service records on the other's vehicle, **Then** the application responds as if the vehicle does not exist.
2. **Given** two signed-in users, **When** one attempts to open, edit, or delete the other's service record by its identifier, **Then** the application responds as if the record does not exist and nothing is changed.
3. **Given** a service record that belongs to one of the user's vehicles, **When** the user tries to open, edit, or delete it through a different vehicle of theirs, **Then** the application responds as if the record does not exist.
4. **Given** a signed-out visitor, **When** they request any service record page, **Then** they are asked to sign in and are returned to it afterwards.
5. **Given** a vehicle with service records, **When** the user deletes the vehicle, **Then** all of its service records are deleted and no orphaned records remain.
6. **Given** a user saving or viewing service records, **When** logs and page addresses are inspected, **Then** no service record contents appear in them.

---

### User Story 5 - Garage mileage reflects service history (Priority: P2)

The vehicle gains an optional "Latest Odometer Reading" field. The mileage shown for each vehicle on the My Vehicles card comes from the latest service record or from that field, so adding a service record keeps the garage mileage current without editing the vehicle.

**Why this priority**: It keeps the garage view accurate, but records, line items, and privacy work without it.

**Independent Test**: Set a Latest Odometer Reading on a vehicle and confirm the card shows it; add a service record with a higher odometer and confirm the card shows that; delete the record and confirm the card returns to the earlier value.

**Acceptance Scenarios**:

1. **Given** a vehicle edit form, **When** the user views Basic Info, **Then** it offers an optional Latest Odometer Reading, and the vehicle detail page shows it (or "Not set").
2. **Given** a vehicle with a Latest Odometer Reading and no service records, **When** the user views My Vehicles, **Then** the card shows that reading as the mileage.
3. **Given** a vehicle whose latest service record has a higher odometer than its Latest Odometer Reading, **When** the user views My Vehicles, **Then** the card shows the service record's odometer.
4. **Given** the user deletes or edits the latest service record, **When** they view My Vehicles, **Then** the card reflects the remaining readings.
5. **Given** a vehicle with neither service records nor a Latest Odometer Reading, **When** the user views My Vehicles, **Then** the card shows the mileage it showed before this feature.
6. **Given** a user filtering My Vehicles by minimum or maximum mileage, **When** they apply the filter, **Then** it uses the same mileage as the card.

---

### User Story 6 - See service history at a glance (Priority: P3)

On the vehicle detail page, the Service Records section gives a quick sense of the history: how many records the vehicle has and when it was last serviced, so users do not have to scroll the list to know where they stand.

**Why this priority**: A convenience on top of the list; the feature is fully usable without it.

**Independent Test**: With a vehicle that has several records, confirm the section shows the correct record count and the most recent service date, and that both update after adding or deleting a record.

**Acceptance Scenarios**:

1. **Given** a vehicle with service records, **When** the user views the Service Records section, **Then** they see the total number of records and the date of the most recent service.
2. **Given** a vehicle whose most recent record is deleted, **When** the user views the section, **Then** the count and most recent service date reflect the remaining records.
3. **Given** a vehicle with no records, **When** the user views the section, **Then** no summary is shown and the empty state is shown instead.

---

### Edge Cases

- A user submits a record with only the required fields: it is saved and every optional field is shown as "Not set".
- A text field contains very long text, control characters, quotes, or angle brackets: length is limited, control characters are rejected, and values are always displayed as plain text.
- A service date in the future is entered: handled by the field rules defined in the plan; the message must say what is wrong.
- Two records for the same vehicle share the same service date: both are kept and listed in a stable order, most recently added first.
- A user opens the Service Records section for a vehicle that no longer exists or never existed: behaves as not found.
- A user opens a service record that was deleted in another browser tab: behaves as not found, with a message and a way back to the vehicle.
- A user edits a record while a session expires: the user is prompted to sign in again, nothing is half-saved, and no other user's data is shown.
- A vehicle has a very large number of records: the list is paged and remains usable on a narrow phone screen without horizontal scrolling of the page.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each service record MUST belong to exactly one vehicle, and each vehicle MUST have its own independent collection of service records.
- **FR-002**: Users MUST be able to add a service record to any of their own vehicles.
- **FR-003**: Users MUST be able to list the service records of one of their own vehicles, most recent service first, with records beyond the first page reachable through paging.
- **FR-004**: Users MUST be able to open a single service record and see every value saved with it, with unset optional values shown as "Not set".
- **FR-005**: Users MUST be able to edit and delete a service record on their own vehicles, with a confirmation step before a delete.
- **FR-006**: The system MUST collect the service record fields defined in the implementation plan for this feature, and the plan's field list is the authoritative definition of which fields are required, optional, and how each is validated.
- **FR-007**: The system MUST validate every service record on add and edit before storing it, MUST store nothing when validation fails, and MUST identify each problem with a specific message next to its field.
- **FR-008**: The system MUST verify server-side, for every service record operation including access by service record identifier, that the record's parent vehicle belongs to the signed-in user; a record or vehicle that is not the user's MUST behave as not found.
- **FR-009**: The system MUST derive the owner from the signed-in user's validated identity and MUST NOT accept any user or owner identifier from the request as a basis for access.
- **FR-010**: The system MUST require sign-in for every service record page and operation; signed-out visitors MUST be asked to sign in and returned to the page afterwards.
- **FR-011**: The system MUST delete all of a vehicle's service records when that vehicle is deleted.
- **FR-012**: The system MUST record who created and last changed each service record and when, attributable to the authenticated user.
- **FR-013**: The vehicle detail page MUST include a Service Records section showing an empty state when there are no records, the list when there are, and the record count and most recent service date when there are.
- **FR-014**: The system MUST NOT write service record contents to logs or place them in page addresses.
- **FR-015**: Service record values MUST always be displayed as plain text so that user-entered content cannot alter the page.
- **FR-016**: The Service Records section and its add, view, and edit screens MUST follow the application's UI standard and remain usable, accessible, and keyboard-operable on narrow phone screens.
- **FR-017**: Existing vehicles and their data MUST continue to work unchanged; vehicles with no service records MUST display normally.
- **FR-018**: Each service record MUST capture a Date Serviced, an Odometer Reading, and an optional free-text Shop Name or Person.
- **FR-019**: Users MUST be able to add, edit, and delete line items on a service record, each identifying the service performed and priced either as a single cost or as a breakdown of labor hours (at an hourly rate) and parts (name, quantity, price per part); a line item MUST NOT use both pricing methods.
- **FR-020**: The system MUST show each line item's cost and the record's total cost as the sum of all its line items.
- **FR-021**: Vehicles MUST have an optional "Latest Odometer Reading" field, and the mileage shown on the My Vehicles card MUST come from the latest service record's odometer or that field, using the greater value; vehicles with neither keep the mileage they show today.

### Key Entities

- **Service Record**: One entry in a vehicle's service history, capturing the fields defined in the implementation plan. It belongs to exactly one vehicle, cannot exist without it, and records who created and last changed it and when.
- **Service Line Item**: One service performed within a record, with its description and cost (single cost, or labor and parts breakdown). It belongs to exactly one record.
- **Part**: One part used in a line item's breakdown, with a name, quantity, and price per part.
- **Vehicle**: An existing entity owned by one user. It gains a collection of zero or more service records, a summary of that collection (count and most recent service date), and a Latest Odometer Reading field.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can add a complete service record to a vehicle in under 2 minutes from opening the vehicle.
- **SC-002**: 90% of users can add, correct, and delete a service record on their first attempt without help.
- **SC-003**: A vehicle with 500 service records still shows its first page of records in under 2 seconds.
- **SC-004**: In testing with two users, 100% of attempts to list, view, add, change, or delete another user's service records are refused as not found, and none of the other user's data is revealed.
- **SC-005**: 100% of invalid submissions are rejected with a message beside each offending field and leave stored data unchanged.
- **SC-006**: Deleting a vehicle leaves zero service records behind for that vehicle.
- **SC-007**: All existing vehicle features continue to work with no change in behavior for vehicles that have no service records.

## Assumptions

- The exact field definitions, which are required, and their validation rules are in the implementation plan's data model; this specification defines the behavior around those fields (add, list, view, edit, delete, privacy).
- Amounts are in a single currency; labor is priced with an hourly rate so that its cost can be totaled.
- Service records are distinct from the existing vehicle journal notes; they do not replace or migrate notes.
- A user's service records are only reachable from within one of their vehicles; there is no cross-vehicle service history view in this feature.
- The default list order is most recent service first; searching, filtering, sorting, exporting, reminders, and cost reporting across records are out of scope for this feature.
- Attachments such as receipts or photos on service records are out of scope unless the plan's field list says otherwise.
- Hard deletion is acceptable; there is no undo or recycle bin for service records.
- Users, vehicles, sign-in, the vehicle detail page, and the UI standard already exist and are reused as they are.
- The feature is used on desktop and phone browsers, with a stable internet connection.
