# Feature Specification: Expanded Vehicle Details and Lookup Preferences

**Feature Branch**: `004-expanded-vehicle-details`

**Created**: 2026-11-01

**Status**: Draft

**Input**: User description: "we need to adjust the vehicle properties and data input needed per vehicle, so this spec will be to incorporate more information."

**Refinement (2026-11-01)**: "The application will need to incorporate various defaults for lookup fields. The application will provide the defaults to each user, but it will permit the user to show/hide/add items in a look up. In the user dropdown menu, we will have a preferences menu and page. On that user preferences page, we will show the account details and a tabbed section where the user can adjust the look up fields. These are the look up fields to create: 1. Vehicle Types: Sedan, SUV, coupe, Hatchback, Convertible, Truck, Van, Wagon, etc. 2. Fuel Types: Gasoline, Diesel, Electric, Hybrid. 3. Vehicle Transmission: Automatic, Manual. 4. Drivetrain: FWD, RWD, AWD, 4WD. The vehicle data fields are going to be listed below, and will need to be placed in the associated tabs: 1. Basic Info: Name, VIN, Make, Model, Trim, Year, Vehicle Type, Purchased or Leased, Date Purchased, Date Lease Ends. 2. Specifications: engine, Horsepower, Fuel Type, Transmission, Mileage, Color, Seats, Doors, Drivetrain, Wheel size, Tire Size."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Record fuller vehicle details on the Basic Info and Specifications tabs (Priority: P1)

A signed-in user adding or editing a vehicle sees the details split across two tabs. **Basic Info** holds Name, VIN, Make, Model, Trim, Year, Vehicle Type, Purchased or Leased, Date Purchased, and Date Lease Ends. **Specifications** holds Engine, Horsepower, Fuel Type, Transmission, Mileage, Color, Seats, Doors, Drivetrain, Wheel Size, and Tire Size. Vehicle Type, Fuel Type, Transmission, and Drivetrain are chosen from the user's lookup lists. After saving, the vehicle's detail page shows every value on the matching tab.

**Why this priority**: Capturing a fuller description of each vehicle is the purpose of this feature; lookups, search, and cards all build on these details.

**Independent Test**: Add a vehicle filling in every field across both tabs, confirm the detail page shows each value on the matching tab; add a second vehicle with only the required fields and confirm it saves and empty fields display gracefully.

**Acceptance Scenarios**:

1. **Given** a signed-in user opening the add-vehicle form, **When** they view it, **Then** they see the Basic Info and Specifications tabs holding exactly the fields listed above, with Name, Year, Make, and Model clearly marked as required.
2. **Given** a user who enters valid values for every field, **When** they save, **Then** the vehicle is stored and its detail page shows each value on the same tab where it was entered.
3. **Given** a user who enters only the required fields, **When** they save, **Then** the vehicle is stored and optional values are shown as "Not set".
4. **Given** a user who enters an invalid value (for example a VIN that is not 17 valid characters, a year in the far future, a negative horsepower, a purchase date in the future, or a lease end date before the purchase date), **When** they save, **Then** nothing is stored, the tab containing the first problem is opened, and each problem is identified with a specific message next to its field.
5. **Given** a user who chooses "Purchased", **When** they enter a Date Lease Ends, **Then** the save is rejected with a message that a lease end date applies only to leased vehicles.
6. **Given** a user who chooses "Leased", **When** they enter a Date Lease Ends, **Then** it is saved and shown on the detail page.
7. **Given** the Vehicle Type, Fuel Type, Transmission, and Drivetrain choices, **When** the user opens each, **Then** it lists only the items currently shown in their lookup (see User Story 2) and an option to leave it unset.

---

### User Story 2 - Start with default lookup lists and customize them (Priority: P1)

Every user starts with the application's default choices for Vehicle Types (Sedan, SUV, Coupe, Hatchback, Convertible, Truck, Van, Wagon, and similar common types), Fuel Types (Gasoline, Diesel, Electric, Hybrid), Transmissions (Automatic, Manual), and Drivetrains (FWD, RWD, AWD, 4WD) without any setup. A user can hide items they never use so dropdowns stay short, show hidden items again, and add their own items (for example "Plug-in Hybrid" or "Dual-clutch"). These changes affect only that user's own lists and dropdowns.

**Why this priority**: The vehicle fields depend on these lists; without defaults a new user could not describe a vehicle, and without customization the lists could not fit every garage.

**Independent Test**: As a new user, open the Vehicle Type dropdown and confirm the defaults are present; hide one, add a custom one, and confirm the dropdown reflects both; sign in as a second user and confirm their lists are unchanged.

**Acceptance Scenarios**:

1. **Given** a user who has never changed any list, **When** they open any of the four dropdowns on a vehicle form, **Then** the application's default items are offered.
2. **Given** a user who hides an item, **When** they open that dropdown, **Then** the hidden item is no longer offered, and it can be shown again at any time.
3. **Given** a user who adds a custom item, **When** they open that dropdown, **Then** the new item is offered after the default items.
4. **Given** a user adding an item whose name matches an existing item ignoring letter case and surrounding spaces (including a hidden default), **When** they submit it, **Then** it is not duplicated and they are told it already exists (and that it is hidden, if so).
5. **Given** a user adding an empty item, an item longer than the allowed length, or one beyond the maximum number of custom items for that list, **When** they submit it, **Then** it is rejected with a clear message.
6. **Given** a vehicle that uses an item the user later hides, **When** the user views or edits that vehicle, **Then** the vehicle still shows its value, and the edit form keeps it selected (marked as hidden) until the user chooses something else.
7. **Given** two different users, **When** one hides or adds items, **Then** the other user's lists and vehicles are not affected.
8. **Given** the application's defaults later gain a new item, **When** any user next opens a dropdown or their preferences, **Then** the new item appears, shown unless that user has hidden it.

---

### User Story 3 - Manage lookups from a Preferences page (Priority: P1)

The user's account dropdown menu has a **Preferences** entry that opens a Preferences page. The page shows the user's account details (display name and email) and, below them, a tabbed section with one tab each for Vehicle Types, Fuel Types, Transmissions, and Drivetrains. Each tab lists all items (including hidden ones), lets the user show or hide each item, and lets them add a new item.

**Why this priority**: This page is the only place the lookup lists are managed, so User Story 2 cannot be used without it.

**Independent Test**: Open the account menu, choose Preferences, confirm account details are shown, switch between the four tabs, hide an item, add an item, and confirm both appear correctly on the tab and in the vehicle form dropdown.

**Acceptance Scenarios**:

1. **Given** a signed-in user, **When** they open the account dropdown menu, **Then** it includes a Preferences entry that opens the Preferences page.
2. **Given** a signed-out visitor, **When** they request the Preferences page, **Then** they are asked to sign in and are returned to it afterwards.
3. **Given** a user on the Preferences page, **When** the page loads, **Then** they see their display name and email as read-only account details and four lookup tabs (Vehicle Types, Fuel Types, Transmissions, Drivetrains), with the first tab open.
4. **Given** a user on a lookup tab, **When** they view the list, **Then** every item is shown with whether it is a default or one they added, and whether it is currently shown or hidden, with a control to show or hide it.
5. **Given** a user on a lookup tab, **When** they add a valid item, **Then** it appears in that list immediately as shown and the same tab stays open.
6. **Given** a user whose add attempt is rejected, **When** the page reloads, **Then** the same tab is open and the problem is explained next to the add field.
7. **Given** a user on a narrow phone screen, **When** they use the page, **Then** the tabs and lists remain usable without horizontal scrolling of the page.

---

### User Story 4 - Existing vehicles keep working (Priority: P2)

A user whose vehicles were saved before this feature continues to see them in the garage and detail pages with the new details showing as "Not set". They can open any such vehicle and fill in the new details over time. Because Year is now required, a vehicle without a year shows a prompt to add one, and saving an edit of such a vehicle requires a year.

**Why this priority**: The garage already holds data that must not be lost or hidden, but new vehicles are not affected.

**Independent Test**: With a vehicle saved before this feature, confirm it still appears in the garage and its detail page, then edit it, add a year and other details, and confirm they are saved and shown.

**Acceptance Scenarios**:

1. **Given** a vehicle saved before this feature, **When** the user views the garage and its detail page, **Then** it appears normally with new details shown as "Not set" and a prompt that the year is missing.
2. **Given** a vehicle without a year, **When** the user saves an edit without adding a year, **Then** the save is rejected with a message that year is required and stored values stay unchanged.
3. **Given** a vehicle with Mileage, Color, and Doors saved before this feature, **When** the user opens its Specifications tab, **Then** those values appear there unchanged.

---

### User Story 5 - See year on cards and find vehicles by the new details (Priority: P2)

Each garage card shows the vehicle's year with its make and model. The garage search also matches the new text details (year, VIN, engine), and the filters add Vehicle Type, Fuel Type, Transmission, Drivetrain, and a year range, combined with the existing filters.

**Why this priority**: The new information is most useful when it can be seen and searched; the details are still valuable on the detail page without it.

**Independent Test**: With several vehicles differing in year, type, fuel, transmission, and drivetrain, confirm cards show the year, searching for a year or engine text returns matches, and each new filter narrows results together with existing filters.

**Acceptance Scenarios**:

1. **Given** a garage with vehicles, **When** the user views the cards, **Then** each shows the year together with the make and model, in addition to the name, mileage, and picture.
2. **Given** a garage, **When** the user enters search text, **Then** vehicles whose name, make, model, trim, color, year, VIN, engine, or any tag contains the text (ignoring letter case) are shown.
3. **Given** a garage, **When** the user chooses a minimum and/or maximum year or a Vehicle Type, Fuel Type, Transmission, or Drivetrain filter, **Then** only vehicles satisfying all chosen filters, existing filters, and search are shown.
4. **Given** the choices for the new lookup-based filters, **When** the user opens them, **Then** only values used by vehicles in their own garage are offered, including values the user has since hidden.
5. **Given** a minimum year greater than the maximum year, **When** the user applies the filters, **Then** they see a clear message and the page remains usable.

---

### User Story 6 - Keep VIN and preferences private (Priority: P3)

A user's VIN, vehicle details, and lookup customizations are visible and changeable only by that user. VIN values never appear in logs or page addresses.

**Why this priority**: Protects sensitive identifiers; it adds no new screens.

**Independent Test**: With two users, confirm one cannot see or change the other's vehicle details, VIN, or custom lookup items, and that VIN values appear in neither logs nor page addresses.

**Acceptance Scenarios**:

1. **Given** two signed-in users, **When** one attempts to view or change the other's vehicle or custom lookup item (by guessing an address or identifier), **Then** the application responds as if it does not exist.
2. **Given** a user saving or viewing a vehicle, **When** logs and page addresses are inspected, **Then** the VIN does not appear.

---

### Edge Cases

- VIN entered in lower case or with surrounding spaces: trimmed and stored in upper case. VIN containing the letters I, O, or Q (never valid): rejected with a specific message.
- Year entered as text or a two-digit value: rejected; year must be a four-digit whole number from 1900 up to next calendar year.
- Date Purchased is in the future: rejected. Date Lease Ends may be past or future but must not be before Date Purchased when both are given.
- Ownership is not chosen but a lease end date is entered: rejected, since a lease end date applies only to leased vehicles.
- Wheel Size is entered outside a plausible range (for example 5 or 80 inches) or with more than one decimal place: rejected with a specific message.
- Horsepower or Seats entered as zero, negative, or an implausibly large number: rejected.
- Engine and Tire Size are free text (for example "2.0L turbo I4" or "225/45R17"); electric vehicles may leave Engine empty or describe the motor.
- A user hides every item in a lookup: allowed; the dropdown then offers only "not set" and the user can add or show items later.
- A custom lookup item name contains very long text, control characters, quotes, or angle brackets: length is limited, control characters are rejected, and values are always displayed as plain text.
- Two vehicles reference the same lookup item: allowed; hiding the item never removes or changes the vehicles using it.
- A user requests another user's custom lookup item or a nonexistent lookup list: behaves as not found.
- A session expires while the user is on a form or on the Preferences page: the user is prompted to sign in again and no other user's data is shown.
- A user opens a vehicle that has since been removed: they see the friendly "vehicle not found" message.
- Search text contains `%`, `_`, quotes, or angle brackets: matched literally as plain text.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The vehicle add and edit forms MUST present the fields on two tabs. Basic Info: Name, VIN, Make, Model, Trim, Year, Vehicle Type, Purchased or Leased, Date Purchased, Date Lease Ends. Specifications: Engine, Horsepower, Fuel Type, Transmission, Mileage, Color, Seats, Doors, Drivetrain, Wheel Size, Tire Size. The vehicle detail page MUST show the same fields on the same tabs.
- **FR-002**: Name, Year, Make, and Model MUST be required; every other vehicle field is optional.
- **FR-003**: The system MUST validate vehicle fields before saving: Name <= 100 characters; Make and Model <= 50; Trim <= 50; Color <= 30; Engine <= 100; Tire Size <= 20; Year a whole number from 1900 to next calendar year; VIN, when given, exactly 17 letters and digits excluding I, O, and Q; Horsepower a whole number 1 to 3,000; Seats a whole number 1 to 20; Doors a whole number 1 to 6; Mileage a whole number 0 to 9,999,999; Wheel Size 10 to 30 inches with at most one decimal place; Date Purchased not in the future; Date Lease Ends not before Date Purchased and only allowed when the vehicle is leased.
- **FR-004**: The system MUST normalize input: trim surrounding spaces from all text, store the VIN in upper case, and treat blank optional values as not set.
- **FR-005**: The system MUST present a specific, user-friendly message for each invalid field, MUST NOT save a vehicle that fails validation, and the form MUST open the tab that holds the first error.
- **FR-006**: "Purchased or Leased" MUST be a choice of exactly Purchased or Leased (or not set).
- **FR-007**: The application MUST provide default lookup items: Vehicle Types (Sedan, SUV, Coupe, Hatchback, Convertible, Truck, Van, Wagon, Minivan, Crossover, Motorcycle); Fuel Types (Gasoline, Diesel, Electric, Hybrid); Transmissions (Automatic, Manual); Drivetrains (FWD, RWD, AWD, 4WD). Every user MUST have these available without any setup.
- **FR-008**: Users MUST be able to hide and re-show any item (default or their own) in each of the four lookups, and to add their own items to each lookup. These changes MUST affect only the user who made them.
- **FR-009**: Added item names MUST be trimmed, 1 to 30 characters, free of control characters, and unique within the lookup for that user ignoring letter case (including against the defaults, hidden or not); a user MUST NOT be able to add more than 50 custom items to one lookup.
- **FR-010**: Lookup dropdowns on the vehicle forms MUST offer only the user's shown items, defaults first in their defined order followed by the user's own items alphabetically, plus an option to leave the field unset.
- **FR-011**: Hiding a lookup item MUST NOT change or remove any vehicle that uses it; a vehicle's edit form MUST keep its current hidden item selected (marked as hidden) until the user changes it, and the detail page and cards MUST keep showing it. A hidden item MUST NOT be newly selectable.
- **FR-012**: The vehicle MUST store a reference to the chosen lookup item (not a copy of its text), so that the chosen item is shown consistently wherever the vehicle appears.
- **FR-013**: A default lookup item added by the application in a later release MUST become available to every user without any action, unless that user has hidden it.
- **FR-014**: The account dropdown menu MUST include a Preferences entry that opens a Preferences page; the page MUST require sign-in.
- **FR-015**: The Preferences page MUST show the user's account details (display name and email) as read-only, followed by a tabbed section with tabs Vehicle Types, Fuel Types, Transmissions, and Drivetrains. Each tab MUST list all items for that lookup (shown and hidden), indicating default or user-added and shown or hidden, with controls to show or hide each item and to add a new item.
- **FR-016**: The Preferences page and the vehicle forms MUST open the relevant tab on request (for example after a rejected change) and MUST be usable with a keyboard and screen reader, with labeled controls, accessible tab semantics and error messages, and MUST adapt to narrow (phone) screens.
- **FR-017**: Existing vehicles MUST remain listed, viewable, and editable after this feature is released, with the new details empty. Existing Mileage, Color, and Doors values MUST be preserved. A vehicle without a year MUST show a prompt to add one, and saving an edit to it MUST require a year.
- **FR-018**: Garage cards MUST show the year together with the make and model.
- **FR-019**: The garage text search MUST additionally match year, VIN, and engine (ignoring letter case, as plain text).
- **FR-020**: The garage MUST add filters for Vehicle Type, Fuel Type, Transmission, Drivetrain, minimum year, and maximum year, combined with all existing filters and search. The lookup-based filter choices MUST be the distinct items used by vehicles in the user's own garage; invalid criteria (for example minimum year greater than maximum year) MUST be rejected with a clear message.
- **FR-021**: The system MUST only reveal, change, or remove a vehicle, its details, a user's custom lookup items, or a user's show/hide choices for that user; requests for another user's data MUST be answered as "not found". Ownership MUST come solely from the verified sign-in, never from request data.
- **FR-022**: The system MUST NOT write the VIN or other vehicle details, or custom lookup item names, to logs, and MUST NOT include them in page addresses.
- **FR-023**: The system MUST record who last changed each vehicle and when, as it does today, including changes to the new details.
- **FR-024**: All new data MUST be stored durably so it is still present after the application is restarted or redeployed.

### Key Entities *(include if feature involves data)*

- **Vehicle**: An existing entity owned by exactly one user. Gains: VIN, year, vehicle type, ownership (purchased or leased), date purchased, lease end date, engine, horsepower, fuel type, transmission, seats, drivetrain, wheel size, tire size. Existing attributes (name, make, model, trim, color, mileage, doors, tags, journal notes, images) are unchanged, though Mileage, Color, and Doors move to the Specifications tab.
- **Lookup**: One of four named lists (Vehicle Types, Fuel Types, Transmissions, Drivetrains) that supply choices for vehicle fields.
- **Lookup Item**: A single choice in a lookup. Is either a default provided by the application (available to everyone) or a custom item added by one user (visible only to that user). Has a name and, for defaults, a defined display order.
- **Lookup Visibility Choice**: A user's decision to hide a particular lookup item (default or custom) from their own dropdowns; absence of a choice means the item is shown.
- **User**: The signed-in owner of vehicles, lookup customizations, and preferences, identified by the existing sign-in feature; account details (display name, email) come from the sign-in and are read-only here.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can add a vehicle with all required details in under 2 minutes, and with every field filled in under 5 minutes.
- **SC-002**: A new user can add a vehicle using lookup dropdowns without any prior setup (100% of the four dropdowns are populated for a user who has never opened Preferences).
- **SC-003**: A user can hide an item or add a custom item from the Account menu in under 30 seconds, and the change is reflected in the next vehicle form they open 100% of the time.
- **SC-004**: 100% of attempts to save a vehicle or add a lookup item with invalid input are rejected with a message identifying the specific field and problem.
- **SC-005**: 100% of vehicles saved before this feature remain visible and openable after release, with no loss of previously stored information.
- **SC-006**: A user can locate a specific vehicle in a garage of 100 vehicles using search or any new filter in under 15 seconds, with results updating in under 2 seconds.
- **SC-007**: 100% of search and filter results satisfy every active criterion and never include another user's vehicles.
- **SC-008**: In testing with at least two users, 100% of attempts to view or change another user's vehicle details, custom lookup items, or visibility choices are refused without revealing that they exist, and no VIN appears in logs or page addresses.
- **SC-009**: 90% of first-time users can find the Preferences page from the account menu and add a custom lookup item without assistance.

## Assumptions

- Builds on vehicle tracking (spec 002), UI branding standards (spec 003), and sign-in (spec 001); tags, journal notes, images, and the privacy rule that a user sees only their own vehicles are unchanged.
- The tab and field layout follows the user's lists exactly. The reference image's Pricing tab is not part of this feature, so purchase price, license plate, and body style (replaced by Vehicle Type) are out of scope; the existing Media and Description tabs are unchanged and keep showing images and tags/journal.
- Required vehicle fields are Name (the existing nickname), Year, Make, and Model. Year is required for new vehicles and whenever an existing vehicle is edited; existing vehicles are never blocked from viewing or listing because the year is missing.
- "Date Purchased" records the acquisition date for both purchased and leased vehicles; "Date Lease Ends" applies only to leased vehicles.
- Vehicle Types default list adds Minivan, Crossover, and Motorcycle to the user's examples ("etc."); the other defaults are exactly as given.
- Users can show/hide and add lookup items but cannot rename or permanently delete items in v1; hiding serves as removal and keeps every vehicle's data intact.
- Custom lookup items are private to the user who added them and may not duplicate a default or the user's other items (ignoring letter case).
- Account details on the Preferences page are read-only and come from the user's sign-in; changing them, notifications settings, and other preferences are out of scope.
- VIN is validated for length and allowed characters only; no check-digit verification and no automatic lookup of vehicle details from a VIN.
- Engine and Tire Size are free text; Horsepower is a whole number; Wheel Size is in inches; Mileage remains a whole number of miles.
- Filters allow one value each for Vehicle Type, Fuel Type, Transmission, and Drivetrain plus a year range; sorting options, bulk edits, and importing vehicles are out of scope.
- Mobile browsers are supported in the same way as desktop browsers; native mobile apps are out of scope.
- Editing uses last-save-wins; no concurrent-edit conflict resolution is required for v1.
