# Feature Specification: Vehicle Tracking

**Feature Branch**: `002-vehicle-tracking`

**Created**: 2026-11-01

**Status**: Draft

**Input**: User description: "One of the primary functions of this application is to track a user's vehicles. This is not going to be like a car rental company with hundreds or thousands of vehicles. Each user will want to be able to track their vehicles with some stats and maybe some images for each."

**Refinement (2026-11-01)**: "A user will have a vehicle listing, which will give them a paged view of their vehicles. It should not be a table structure. It should be a card based structure to view a vehicle name, make, model, mileage, and picture to start. The vehicle will consist of things like nickname, Make, Model, trim, color, mileage, doors, and then a section that lets someone create free text tags to describe features of the vehicle. There can be a journaling notes section with a tagging feature on a new note to identify a category of the note. We will now use the database to store this vehicle information. At this time, there is no sharing of vehicles between users. A logged in user will only see their vehicles."

**Refinement 2 (2026-11-01)**: "Each vehicle can include a small gallery of image uploads. I think these images should live in a storage account. Journal categories should not be predefined, they can be open text so the user can determine what to use. The vehicle listing page should include a search and filtering feature to help locate a vehicle with specific criteria."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Add vehicles and browse them as cards (Priority: P1)

A signed-in user opens their garage and sees the vehicles they own as a paged set of cards (not a table). Each card shows the vehicle's picture (or a placeholder), nickname, make, model, and mileage. They can add a new vehicle by entering its details (nickname, make, model, trim, color, mileage, number of doors), then see it appear as a card and open it to view all of its details.

**Why this priority**: Recording and browsing vehicles is the core purpose of the application and the minimum viable slice. Tags, journal notes, and images all depend on vehicles existing.

**Independent Test**: Sign in as a user with no vehicles, add one vehicle with its details, and confirm it appears as a card and its detail page shows everything that was entered. Add enough vehicles to exceed one page and confirm paging works.

**Acceptance Scenarios**:

1. **Given** a signed-in user with no vehicles, **When** they open their garage, **Then** they see a friendly empty state with a clear way to add their first vehicle.
2. **Given** a signed-in user, **When** they submit the add-vehicle form with valid details, **Then** the vehicle is saved, appears as a card in their garage, and its details can be viewed.
3. **Given** a signed-in user, **When** they submit the add-vehicle form with missing required details or invalid values (for example a negative mileage or an unrealistic number of doors), **Then** the vehicle is not saved and they see clear messages identifying each problem.
4. **Given** a user with more vehicles than fit on one page, **When** they view their garage, **Then** they see the first page of cards with controls to move to the next and previous pages and an indication of the current page and total.
5. **Given** two different signed-in users, **When** each views their garage, **Then** each sees only the vehicles they added themselves.

---

### User Story 2 - Search and filter the garage (Priority: P2)

On the garage page, a user can type search text and/or choose filters to narrow the cards to the vehicles that match, for example "show me every red truck with a tow package" or "vehicles with more than 100,000 miles". They can see which criteria are active and clear them with one action. Paging continues to work within the matching results.

**Why this priority**: Garages are small but still grow past a page; finding a specific vehicle quickly makes the listing useful. It builds on the card listing and is not needed to add or view vehicles.

**Independent Test**: With several vehicles of different makes, colors, mileages, and tags, enter search text and then each filter and confirm only matching vehicles are shown, with a result count and a way to clear.

**Acceptance Scenarios**:

1. **Given** a garage with several vehicles, **When** the user enters search text, **Then** only vehicles whose nickname, make, model, trim, color, or any tag contains that text (ignoring letter case) are shown.
2. **Given** a garage, **When** the user chooses filters (make, color, number of doors, tag, minimum and/or maximum mileage), **Then** only vehicles matching all chosen filters (and the search text, if any) are shown.
3. **Given** active search or filters, **When** the results span more than one page, **Then** paging controls move through the matching results and keep the search and filters applied.
4. **Given** active search or filters that match nothing, **When** the results are shown, **Then** the user sees a "no vehicles match" message with a clear way to reset the search and filters, distinct from the empty-garage state.
5. **Given** active search or filters, **When** the user clears them, **Then** the full garage is shown again from the first page.
6. **Given** a user choosing a filter, **When** they open the filter choices for make, color, and tag, **Then** the choices offered are only values present in their own garage.
7. **Given** invalid criteria (for example a minimum mileage greater than the maximum, or a negative mileage), **When** they apply them, **Then** they see a clear message and the previous results are not replaced by an error page.

---

### User Story 3 - Edit and remove a vehicle (Priority: P2)

A user can correct or update a vehicle's details (for example, updating the mileage after a trip or changing its color) and can remove a vehicle they no longer own after confirming the action.

**Why this priority**: Vehicle details change over time and mistakes happen. This keeps the data accurate, but the application is still useful with add and view alone.

**Independent Test**: Edit an existing vehicle's details and confirm the changes are shown; then delete a vehicle after confirmation and confirm it no longer appears in the list.

**Acceptance Scenarios**:

1. **Given** a user viewing one of their vehicles, **When** they change details and save with valid values, **Then** the updated details are shown on the detail page and in the list.
2. **Given** a user editing a vehicle, **When** they save invalid values, **Then** the changes are rejected with clear messages and the stored details remain unchanged.
3. **Given** a user viewing one of their vehicles, **When** they choose to remove it, **Then** they are asked to confirm, and on confirmation the vehicle and everything attached to it (tags, journal notes, images) are removed and no longer appear in their garage.
4. **Given** a user who starts to remove a vehicle, **When** they cancel the confirmation, **Then** the vehicle is kept unchanged.

---

### User Story 4 - Describe a vehicle with free-text tags (Priority: P2)

On a vehicle's detail page, a user can add free-text tags that describe features of the vehicle (for example "sunroof", "tow package", "heated seats"), see them displayed with the vehicle, and remove tags they no longer want.

**Why this priority**: Tags capture the features that do not fit fixed fields and make each vehicle feel complete, but the garage works without them.

**Independent Test**: Open a vehicle, add a tag, confirm it is shown on the vehicle, remove it, and confirm it is gone.

**Acceptance Scenarios**:

1. **Given** a user viewing one of their vehicles, **When** they enter a tag and add it, **Then** the tag is shown with the vehicle's other tags.
2. **Given** a vehicle that already has a tag, **When** the user adds the same tag again (ignoring letter case and surrounding spaces), **Then** no duplicate is created and the user is told the tag already exists.
3. **Given** a user adding a tag that is empty or longer than the allowed length, or a vehicle already at the maximum number of tags, **When** they submit it, **Then** it is rejected with a clear message.
4. **Given** a vehicle with tags, **When** the user removes one tag, **Then** only that tag is removed.

---

### User Story 5 - Keep a journal of notes about a vehicle (Priority: P2)

On a vehicle's detail page, a user can keep a journal: they write a new note, optionally give it a category of their own choosing in free text (for example "Oil change", "Road trip", or "Detailing"), and see the vehicle's notes listed newest first, each showing its category, text, and the date it was written. They can edit or delete their own notes.

**Why this priority**: A running journal is the main way a user records the history of a vehicle over time, but it builds on vehicles existing and is not needed to browse the garage.

**Independent Test**: Open a vehicle, add a note with a category you make up, confirm it appears at the top of the journal with its category and date, then edit and delete it.

**Acceptance Scenarios**:

1. **Given** a user viewing one of their vehicles, **When** they write a note, type a category of their choice, and save, **Then** the note appears at the top of that vehicle's journal showing its category, text, and date.
2. **Given** a user creating a note, **When** they leave the category blank, **Then** the note is saved without a category and is shown as uncategorized.
3. **Given** a user submitting a note that is empty or longer than the allowed length, or a category longer than the allowed length, **When** they save it, **Then** it is rejected with a clear message and nothing is stored.
4. **Given** a vehicle with many notes, **When** the user views its journal, **Then** notes are shown newest first and are paged so the page stays manageable.
5. **Given** a vehicle with notes in different categories, **When** the user views the journal, **Then** each note's category is clearly visible.
6. **Given** a user viewing a note, **When** they edit its text or category, or delete it (after confirmation), **Then** the change is reflected in the journal.

---

### User Story 6 - Add a small gallery of images to a vehicle (Priority: P3)

A user can upload a small gallery of pictures for a vehicle (up to 10), see them on the vehicle's detail page, choose one as the main picture shown on the garage card, and remove pictures they no longer want.

**Why this priority**: Pictures make the garage feel personal and help identify vehicles at a glance, but the stats-based tracking delivers value without them.

**Independent Test**: Open a vehicle, upload a valid image, confirm it shows on the detail page and can be set as the main picture shown in the list, then remove it.

**Acceptance Scenarios**:

1. **Given** a user viewing one of their vehicles, **When** they upload a valid image, **Then** the image appears in that vehicle's image gallery.
2. **Given** a vehicle with multiple images, **When** the user chooses one as the main image, **Then** that image is shown for the vehicle in the garage list.
3. **Given** a user uploading a file that is not a supported image type or is larger than the allowed size, **When** they submit it, **Then** the upload is rejected with a message explaining the allowed types and size, and nothing is stored.
4. **Given** a vehicle with images, **When** the user removes one image, **Then** only that image is removed; if it was the main image, another remaining image (if any) becomes the main image.
5. **Given** a vehicle that has no images, **When** it is shown in the list or on its detail page, **Then** a neutral placeholder is shown instead.

---

### Edge Cases

- A user tries to open, edit, delete, or add images to a vehicle that belongs to someone else (by guessing its address): the application behaves as if the vehicle does not exist and reveals nothing about it.
- A user opens a vehicle that has since been removed (for example in another browser tab): they see a friendly "vehicle not found" message.
- A user asks for a garage page beyond the last page, or a page number that is not valid: they are shown the nearest valid page rather than an error.
- Search text contains characters such as `%`, `_`, quotes, or angle brackets: they are matched literally as plain text and never change how the search works.
- Search text is only spaces, or is longer than the allowed length: spaces-only is treated as no search; over-length input is rejected with a clear message.
- Search or filters narrow the results so that the page the user was on no longer exists: the nearest valid page of the matching results is shown.
- A user types a category or tag with extra surrounding spaces (for example " Oil Change "): the surrounding spaces are removed and the letter case is kept as entered; tags that differ only in letter case count as the same tag.
- Two vehicles share the same nickname: both are allowed, and cards are ordered consistently so paging never skips or repeats a vehicle.
- The user reaches the maximum number of images or tags for a vehicle: further additions are rejected with a message stating the limit.
- A user tries to read, edit, or delete a note or tag that belongs to another user's vehicle (by guessing its address): it behaves as not found.
- Text fields contain very long values or characters such as quotes and angle brackets: values are limited in length, stored as entered, and always displayed as plain text without being interpreted as markup.
- A session expires while the user is filling in the add or edit form: the user is prompted to sign in again and is not shown another user's data.
- An image upload is interrupted or fails: no partial image is shown and the user is told the upload failed and can retry.
- Image storage is temporarily unavailable: image uploads and picture display fail gracefully (placeholder shown, clear message on upload) while vehicle details, tags, and notes remain usable.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each vehicle MUST belong to exactly one user, the signed-in user who created it.
- **FR-002**: Users MUST be able to see their own vehicles, and only their own, as a paged set of cards (not a table), each showing the vehicle's main image (or a placeholder), nickname, make, model, and mileage.
- **FR-002a**: The garage MUST show a fixed number of cards per page (default 12), ordered consistently, with controls for next and previous pages and the current page and total pages; out-of-range page requests MUST resolve to the nearest valid page.
- **FR-003**: Users MUST be able to add a vehicle with required details (nickname, make, model) and optional details (trim, color, mileage, number of doors).
- **FR-004**: Users MUST be able to view all stored details, tags, journal notes, and images of one of their vehicles on a detail page.
- **FR-005**: Users MUST be able to edit any detail of one of their vehicles.
- **FR-006**: Users MUST be able to remove one of their vehicles after explicit confirmation, which also removes all of that vehicle's tags, journal notes, images, and any other data attached to it.
- **FR-007**: The system MUST validate all vehicle details before saving, including: required fields present; mileage not negative and within a plausible maximum; number of doors a whole number within a plausible range (1 to 6); and text fields within defined length limits.
- **FR-008**: The system MUST present specific, user-friendly validation messages for each invalid field, and MUST NOT save a vehicle that fails validation.
- **FR-009**: Users MUST be able to add free-text tags to one of their vehicles and remove them; tags MUST be unique per vehicle ignoring letter case and surrounding spaces, non-empty, and limited in length and in number per vehicle.
- **FR-009a**: Users MUST be able to add journal notes to one of their vehicles; each note MUST have text, an optional category entered as free text by the user, and MUST record when it was written.
- **FR-009b**: Users MUST be able to edit and delete journal notes on their own vehicles (deletion after confirmation); the journal MUST list notes newest first and be paged.
- **FR-009c**: Journal categories MUST NOT be predefined; the system MUST accept any non-empty text up to the allowed length (trimmed) and MUST treat a blank category as "uncategorized".
- **FR-010**: Users MUST be able to upload pictures to one of their vehicles to form a small gallery, limited to common web image formats (JPEG, PNG, WebP), a maximum size per image, and a maximum number of images per vehicle (10).
- **FR-011**: The system MUST verify the content of uploaded files is actually an allowed image type, regardless of the file name or type claimed by the user.
- **FR-012**: Users MUST be able to choose which image is the main image of a vehicle and to remove individual images.
- **FR-013**: The system MUST only reveal, change, or remove a vehicle or its tags, journal notes, or images for the user who owns it; a request for another user's vehicle or any of its parts MUST be answered as "not found" and MUST NOT disclose that it exists.
- **FR-014**: Ownership MUST be determined solely from the verified sign-in of the requesting user, never from any user identifier supplied in a request.
- **FR-015**: All vehicle and image functionality MUST require the user to be signed in.
- **FR-016**: Vehicle details, tags, and journal notes MUST be stored durably in the application's database, and uploaded image files MUST be stored durably in the application's cloud storage account (not on the web or API server's disk), so everything is still present after the application is restarted or redeployed.
- **FR-017**: The system MUST record who created and last changed each vehicle and journal note and when.
- **FR-018**: The system MUST NOT write vehicle details, tags, journal note text, or image content to logs.
- **FR-019**: The garage and detail pages MUST be usable with a keyboard and screen reader, with text alternatives for images and labeled form controls, and MUST adapt to narrow (phone) screens.
- **FR-020**: Vehicles MUST NOT be shared between users; there is no feature to view, copy, or transfer another user's vehicles.
- **FR-021**: The garage page MUST provide a text search that matches (ignoring letter case, as plain text) a vehicle's nickname, make, model, trim, color, or any of its tags.
- **FR-022**: The garage page MUST provide filters for make, color, number of doors, tag, minimum mileage, and maximum mileage; all chosen filters and the search text MUST be combined (a vehicle must satisfy all of them).
- **FR-023**: Search and filtering MUST apply only to the signed-in user's own vehicles, MUST apply before paging, and the paging controls and total count MUST reflect the matching results while keeping the search and filters in place.
- **FR-024**: The garage page MUST show which search/filter criteria are active, MUST offer a single action to clear them, and MUST distinguish "no vehicles yet" from "no vehicles match".
- **FR-025**: The choices offered for the make, color, and tag filters MUST be the distinct values present in the user's own garage; invalid criteria MUST be rejected with a clear message.
- **FR-026**: Uploaded image files MUST be kept private: they MUST be readable only through the application on behalf of the owning user and MUST NOT be reachable by a public address.

### Key Entities *(include if feature involves data)*

- **Vehicle**: A car, truck, motorcycle, or similar vehicle owned by one user. Attributes: nickname, make, model, trim, color, mileage, number of doors, main image, who created/changed it and when. Belongs to exactly one user; has zero or more tags, journal notes, and images.
- **Vehicle Tag**: A short free-text label describing a feature of a vehicle (for example "sunroof"). Belongs to exactly one vehicle; unique per vehicle ignoring letter case.
- **Journal Note**: A dated free-text entry about a vehicle with an optional user-defined free-text category. Attributes: text, category (optional), who wrote it and when, when last edited. Belongs to exactly one vehicle.
- **Vehicle Image**: A picture of a vehicle, part of the vehicle's small gallery. Attributes: the image file (kept in the cloud storage account), an optional caption or description, whether it is the main image, when it was added. Belongs to exactly one vehicle.
- **User**: The signed-in owner of vehicles, identified by their verified sign-in identity (established by the existing sign-in feature).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A signed-in user can add a new vehicle with its basic details in under 2 minutes.
- **SC-002**: Any page of a user's garage (up to 12 cards with images) appears in under 2 seconds under normal use, for garages of up to 100 vehicles.
- **SC-008**: A user can add a tag or a journal note (with or without a category) to a vehicle in under 30 seconds.
- **SC-009**: A user can find a specific vehicle in a garage of 100 vehicles using search or filters in under 15 seconds, and results update in under 2 seconds.
- **SC-010**: 100% of search and filter results contain only vehicles that satisfy every active criterion, and never include another user's vehicles.
- **SC-003**: In testing with at least two different users, 100% of attempts by one user to view, change, or delete another user's vehicle, tags, journal notes, or images are refused without revealing that they exist.
- **SC-004**: 100% of attempts to save a vehicle with invalid details, or to upload a disallowed file, are rejected with a message that identifies the specific problem.
- **SC-005**: A user can upload an image of typical phone-camera size and see it on the vehicle within 10 seconds.
- **SC-006**: 90% of first-time users can add a vehicle and upload a picture of it without assistance.
- **SC-007**: After the application is restarted, 100% of previously saved vehicles, tags, journal notes, and images are still present and unchanged.

## Assumptions

- Users are already able to sign in and are identified by the existing sign-in feature (spec 001); no new sign-in or account features are in scope.
- Each user is expected to track a small number of vehicles (a handful to a few dozen); paging, search, and filtering are provided for usability, while custom sort options and bulk operations are out of scope for v1 (the garage is ordered by nickname).
- Search is simple case-insensitive "contains" matching on the listed fields; fuzzy matching, ranking, and searching journal notes are out of scope for v1. Filters allow one value each for make, color, doors, and tag, plus a mileage range.
- Sharing vehicles between users, transferring ownership, and any public or shared view of a garage are out of scope; every garage is private to its owner.
- Service records, reminders, costs, VIN, license plate, and purchase details are separate future features or additions; this feature only provides the vehicle they will later attach to.
- Mileage is a whole number in miles, entered manually; unit selection and automatic mileage sources are out of scope.
- Journal notes are plain text, are not versioned, and have no attachments; a note has at most one free-text category (up to 40 characters). Filtering or searching notes by category is out of scope for v1.
- Tags are plain text; tag search is part of the garage search and tag filter (FR-021, FR-022).
- Default limits: 20 tags per vehicle (30 characters each), 4,000 characters per note, 10 notes per journal page.
- Supported image formats are JPEG, PNG, and WebP, with a default limit of 5 MB per image and 10 images per vehicle; these limits can be adjusted during planning.
- Vehicle images are viewable only by the owning user and are not publicly accessible by address.
- Mobile browsers are supported in the same way as desktop browsers; native mobile apps are out of scope.
- Editing a vehicle uses last-save-wins; no concurrent editing conflict resolution is required for v1.
