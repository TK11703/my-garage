# Feature Specification: Window Sticker Upload Shortcut

**Feature Branch**: `010-window-sticker-upload`

**Created**: 2026-11-05

**Status**: Draft

**Input**: User description: "when creating a vehicle, I want to be able to offer a shortcut option of \"upload the window sticker\". This will help populate a majority of the vehicle information."

**Amendment 1 (2026-11-05, planning input)**: The shortcut is an "Import from sticker" tab, the first tab when adding a vehicle and when editing one; the user picks a file, sees it in the tab, and presses an accept button before anything is read; the results fill the Details tab; and features found on the sticker replace duplicate features the vehicle already has. See **Amendment 1** at the end of this document; where it conflicts with the text above (an option on the page, add-only, no overwrite without telling the user), the amendment wins.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Start a new vehicle from its window sticker (Priority: P1)

A signed-in user who is adding a vehicle is offered a shortcut at the start of the add-vehicle page: "Upload the window sticker". The user chooses a photo, scan, or PDF of the vehicle's window sticker (the label showing the vehicle's specifications and equipment). My Garage reads the document and fills in the add-vehicle form with the details it finds: year, make, model, trim, VIN, vehicle type, engine, horsepower, fuel type, transmission, drivetrain, color, seats, doors, wheel size, tire size, and the listed features. The user then reviews the filled-in form, corrects or completes anything, adds details that are not on a sticker (for example ownership, purchase date, and mileage), and saves. Nothing is saved until the user saves the form.

**Why this priority**: This is the whole point of the feature. It removes most of the typing needed to add a vehicle and is useful on its own even if nothing else here is built.

**Independent Test**: Open the add-vehicle page, upload a window sticker, confirm the form is filled with the values shown on the sticker, change one value, save, and confirm the vehicle's detail page shows the filled-in and corrected values on the matching tabs.

**Acceptance Scenarios**:

1. **Given** a signed-in user on the add-vehicle page, **When** they view it, **Then** they see an "Upload the window sticker" option alongside the normal form, and the normal form remains fully usable without it.
2. **Given** a user who uploads a readable window sticker, **When** it has been read, **Then** the form shows each detail found in the matching field and tab, and the user is told that the details came from the sticker and should be reviewed.
3. **Given** a form filled in from a sticker, **When** the user edits any value or fills in fields the sticker did not provide and saves, **Then** the vehicle is saved with the user's final values using the same rules and messages as a vehicle entered by hand.
4. **Given** a form filled in from a sticker, **When** the user leaves the page without saving, **Then** no vehicle, feature, or file is created or kept.
5. **Given** a sticker that lists only some of the details, **When** it has been read, **Then** the fields it did not provide stay empty, and fields the user had already filled in before uploading are not overwritten without the user being told.
6. **Given** a sticker value that matches an item in the user's Vehicle Type, Fuel Type, Transmission, or Drivetrain lists (ignoring letter case and spacing), **When** the form is filled in, **Then** that item is selected; **Given** a value that matches no item, **Then** the field is left unset and the text read from the sticker is shown next to it so the user can pick or add an item.
7. **Given** a sticker that lists equipment and options, **When** it has been read, **Then** they are proposed as the vehicle's features, within the existing limits on the number and length of features, and the user can remove any before saving.

---

### User Story 2 - Be told clearly when the sticker cannot be read (Priority: P1)

A user uploads a file that cannot be used: it is the wrong type, too large, damaged, unreadable (blurry, cropped, or glare), or does not look like a window sticker. The user is told what went wrong and what to try, and the add-vehicle form stays exactly as it was so they can try another file or enter the details by hand.

**Why this priority**: A shortcut that fails silently or loses what the user typed is worse than no shortcut. Clear failure handling is needed for the feature to be trusted.

**Independent Test**: Upload an unsupported file, an oversized file, a corrupt file, and a photo that is not a window sticker; confirm each produces a specific message, no form values are lost or wrongly filled, and the user can continue by hand.

**Acceptance Scenarios**:

1. **Given** a file of an unsupported type, a file over the size limit, or a damaged file, **When** the user uploads it, **Then** it is refused with a message that names the problem and the accepted file types and size, and the form is unchanged.
2. **Given** a readable file that is not a window sticker or from which nothing useful can be read, **When** it has been processed, **Then** the user is told that no vehicle details were found, the form is unchanged, and they can upload another file or continue by hand.
3. **Given** a sticker from which only a few values could be read with confidence, **When** the form is filled in, **Then** only those values are filled in, and the user is told that some details could not be read.
4. **Given** the reading service is unavailable or takes too long, **When** the user uploads a sticker, **Then** they are told the shortcut is unavailable right now, the form is unchanged, and they can enter the details by hand without losing anything.
5. **Given** a user who is waiting for a sticker to be read, **When** it takes more than a moment, **Then** they see that work is in progress and cannot accidentally submit the form twice or lose what they have typed.

---

### User Story 3 - Keep the sticker private (Priority: P2)

A window sticker contains the VIN and other identifying information. A user's uploaded sticker is used only to fill in that user's form, is visible only to them, is not kept after the details have been read, and is never exposed to other users or written to logs.

**Why this priority**: It protects sensitive identifiers, but adds no new screens and the shortcut delivers value without it being separately demonstrable.

**Independent Test**: Upload a sticker as one user; confirm that after the form is filled in no copy of the file remains in storage and that no other user can reach it or the details read from it; inspect logs and page addresses and confirm the VIN and other sticker contents do not appear.

**Acceptance Scenarios**:

1. **Given** a sticker that has been read, **When** the form has been filled in (or reading failed), **Then** the uploaded file is no longer kept anywhere.
2. **Given** two signed-in users, **When** one uploads a sticker, **Then** the other cannot see the file, the details read from it, or the progress of its reading.
3. **Given** a signed-out visitor or an expired session, **When** they attempt to upload a sticker, **Then** they are asked to sign in and nothing is read or kept.
4. **Given** a sticker being uploaded or read, **When** logs and page addresses are inspected, **Then** neither the VIN nor any other detail from the sticker appears.

---

### Edge Cases

- The same VIN is read from a sticker that already belongs to a vehicle in the user's garage: the user is warned that a vehicle with that VIN already exists, and can still choose to continue or cancel.
- A value read from the sticker fails the normal validation (for example a VIN that is not 17 valid characters, a year in the far future, a wheel size outside the plausible range): the invalid value is not placed in the form as if it were good; the user is shown what was read and which field needs attention, and the rules for saving are unchanged.
- The sticker lists more than 100 features, or a feature longer than the allowed length: the first 100 usable features are proposed and the user is told some were left out; over-long names are shortened or dropped according to the existing feature rules.
- The sticker lists options that are not equipment (for example prices, fees, fuel economy ratings, or dealer information): these are ignored except where they match a vehicle field; prices and fuel economy are not stored in this feature.
- A multi-page PDF where the sticker is not on the first page, or an image containing several stickers: the user is told if more than one vehicle seems to be present, and only one vehicle's details are ever filled in.
- The user uploads a second sticker after the first has filled in the form: they are asked, or told, that the values from the new sticker replace those from the previous one but never the values they typed themselves unless they agree.
- The user uploads a photo taken sideways or at an angle: reading is attempted as-is; if it fails, the failure message suggests a clearer, upright photo.
- Text read from the sticker contains angle brackets, quotes, or control characters: it is always displayed as plain text and subject to the same field rules as typed input.
- The user's session expires while a sticker is being read: the user is prompted to sign in again and no other user's data is shown.
- A user who adds vehicles in different languages or with metric units on the sticker: values are filled in only where they can be interpreted unambiguously in the units the form uses; otherwise they are left for the user.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The add-vehicle page MUST offer an "Upload the window sticker" shortcut in addition to the normal form; the normal form MUST remain fully usable without it.
- **FR-002**: The shortcut MUST accept PDF files and common image files (JPEG, PNG, and WebP), identify the file type from the file's actual content rather than its name, and refuse other types, damaged files, and files over the stated size limit with a message that names the problem.
- **FR-003**: The system MUST read an accepted file and extract the vehicle details present on it among: year, make, model, trim, VIN, vehicle type, engine, horsepower, fuel type, transmission, drivetrain, color, seats, doors, wheel size, tire size, and features.
- **FR-004**: The system MUST place the extracted details into the corresponding fields of the add-vehicle form, on the tabs where those fields normally appear, and MUST NOT save anything until the user saves the form.
- **FR-005**: The system MUST leave a field empty when its value is not on the sticker or cannot be read with confidence, rather than guessing, and MUST tell the user that the filled-in details came from the sticker and should be reviewed.
- **FR-006**: Values for Vehicle Type, Fuel Type, Transmission, and Drivetrain MUST be matched to the items in the user's own lists (ignoring letter case and surrounding spaces); an unmatched value MUST leave the field unset and show the text read so the user can choose or add an item. The shortcut MUST NOT add items to the user's lists on its own.
- **FR-007**: Extracted values MUST be subject to the same validation as values typed by the user; a value that would fail validation MUST NOT be presented as accepted, and saving MUST apply the same rules and messages as for a vehicle entered by hand.
- **FR-008**: Extracted features MUST follow the existing feature rules (shared by name, 1 to 30 characters, at most 100 per vehicle, no duplicates) and MUST be removable by the user before saving.
- **FR-009**: Values the user has already entered MUST NOT be overwritten by a sticker's values without the user being told and given the choice.
- **FR-010**: If the file cannot be used, is not a window sticker, yields no vehicle details, or the reading service is unavailable or too slow, the system MUST say so clearly, MUST leave the form and everything the user has typed unchanged, and MUST let the user try again or continue by hand.
- **FR-011**: While a sticker is being read, the user MUST see that work is in progress, and the form MUST NOT be submitted twice or lose its content as a result.
- **FR-012**: When a VIN read from the sticker matches a vehicle already in the user's garage, the system MUST warn the user before they save; it MUST NOT reveal anything about any other user's vehicles.
- **FR-013**: The shortcut MUST require a signed-in user, and the uploaded file and everything read from it MUST be visible only to that user. Identity MUST come from the verified sign-in, never from request-supplied identifiers.
- **FR-014**: The uploaded file MUST NOT be kept after it has been read or reading has failed, and MUST NOT be used for anything other than filling in that user's form.
- **FR-015**: The VIN and other sticker contents MUST NOT appear in logs or page addresses.
- **FR-016**: The shortcut MUST work with the form on narrow phone screens, where a user may take a photo of the sticker directly with the device camera, and MUST be usable with a keyboard and screen reader, with a clear label for the upload control and for progress and error messages.
- **FR-017**: Text read from a sticker MUST always be displayed as plain text.

### Key Entities *(include if feature involves data)*

- **Window Sticker Upload**: A temporary file supplied by a signed-in user to start a new vehicle. It has a type, a size, and an owner (the uploading user), and exists only for as long as it takes to read it.
- **Extracted Vehicle Details**: The set of values read from a window sticker (the vehicle fields listed in FR-003, plus a list of features and any text that could not be matched to a lookup item). They exist only to fill in the add-vehicle form for the owner and are not part of any vehicle until the user saves.
- **Vehicle**: Existing entity; a vehicle created through the shortcut is indistinguishable from one entered by hand once saved.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a readable window sticker, at least 80% of the vehicle fields that are printed on it are filled in correctly in the form, measured against a test set of representative stickers.
- **SC-002**: A user can go from opening the add-vehicle page to a saved vehicle in under 2 minutes using a sticker, compared with the typical time to enter the same details by hand.
- **SC-003**: After uploading a readable sticker, the filled-in form appears within 30 seconds for 95% of uploads, and the user always sees progress while waiting.
- **SC-004**: 100% of failed or unusable uploads (wrong type, too large, damaged, not a sticker, service unavailable) leave the user's form unchanged and show a message that identifies the problem.
- **SC-005**: No vehicle, feature, or lookup item is created from a sticker unless the user saves the form, and no uploaded sticker remains stored after reading completes.
- **SC-006**: No other signed-in user can see another user's sticker, its extracted details, or its reading progress, and the VIN never appears in logs or page addresses.
- **SC-007**: At least 90% of first-time users who try the shortcut on a readable sticker complete saving the vehicle without needing to retype fields that were filled in correctly.

## Assumptions

- The shortcut applies only when adding a new vehicle; using a sticker to update an existing vehicle is out of scope for this version.
- The user supplies one document per upload. Photos, scans, and PDFs of a window sticker are supported; other documents (for example a bill of sale or registration) are out of scope.
- The user's uploaded file is discarded after reading. Keeping the original sticker with the vehicle (for example in the media area) is out of scope for this version.
- Fields that a window sticker does not normally contain (ownership type, purchase or lease date, lease end, mileage, pictures, and journal entries) are always entered by the user.
- Price, fuel-economy ratings, safety ratings, and dealer information printed on a sticker are not stored by this feature.
- File types and limits are PDF, JPEG, PNG, and WebP up to a size stated on the page; the exact limit is set in the plan and is no smaller than the existing 5 MB limit for vehicle pictures.
- Reading a document relies on an automated document-reading capability that the application already uses for email attachments, and a result may be incomplete or wrong; for that reason the user always reviews the form before saving.
- Existing validation, lookup-list, feature, and ownership rules for vehicles apply unchanged to a vehicle created through the shortcut.
- Mobile browsers are supported in the same way as desktop browsers; native mobile apps are out of scope.

## Amendment 1 (2026-11-05)

### Changes requested

1. **Import from sticker tab when adding.** The add-vehicle form has an "Import from sticker" tab placed before the Details tab. It is the tab open when the page first loads, so the shortcut is offered up front; the Details tab remains one click away and the form is fully usable without importing.
2. **Import from sticker tab when editing.** The edit form for an existing vehicle has the same tab as its first tab (before Details, Media, and Description). The edit page still opens on Details unless the user chooses another tab, so existing links and habits are unchanged.
3. **Choose, see, accept.** The tab offers a file chooser (and drop area) for a PDF or image of the sticker. The chosen file is shown in the tab (an image preview, or an embedded view of a PDF, otherwise its name and size) before anything is read. An **Accept import** button then submits the file for reading; nothing is read or sent until the user presses it, and a different file can be chosen first.
4. **Results fill the Details tab.** After the import, the page opens on the Details tab with the values read from the sticker in the matching fields (the existing values of an edited vehicle are replaced by every value the sticker provides and kept for every value it does not), and the Import from sticker tab states what was read and what was not.
5. **Features are consolidated.** Features read from the sticker are proposed for the vehicle. Any feature the vehicle already has that duplicates a proposed one (the same feature worded differently, such as "Sunroof" and "Panoramic Sunroof", or the same wording in a different letter case) is replaced by the sticker's version rather than listed twice. The user sees each proposed feature, and which existing features it would replace, and can leave any out before saving.

### Requirements

- **FR-018**: The add-vehicle and edit-vehicle forms MUST each include an "Import from sticker" tab as their first tab. The add form MUST open on it; the edit form MUST open on Details unless another tab is requested. This replaces the separate option described in FR-001.
- **FR-019**: The tab MUST let the user choose a file by browsing or dropping it, MUST show the chosen file before it is read, and MUST NOT read or transmit it for extraction until the user presses an "Accept import" button. The tab MUST still work without a script (the file name is shown after choosing and the button submits it); the preview is an enhancement.
- **FR-020**: On an edit, a value read from the sticker MUST replace the form's current value for that field; fields the sticker did not provide MUST keep their current values. Before pressing Accept the user MUST be told that the form's unsaved changes are replaced by the saved vehicle plus the sticker's values. Nothing is saved until the user saves the form. This replaces FR-009.
- **FR-021**: Features read from the sticker MUST be shown on the Details tab as proposed features, each selectable and selected by default, together with the vehicle's existing features they would replace. Saving the form MUST add the selected proposed features to the vehicle and remove the existing features they replace, in the same save as the other details; leaving a proposal unselected MUST leave the vehicle's features it would replace unchanged.
- **FR-022**: Two features MUST be treated as duplicates when they are equal ignoring letter case, spacing, and punctuation, or when the reading step reports that the existing one describes the same equipment as the sticker's (for example a shorter or longer wording). The reading step MUST only be able to name features the vehicle currently has; anything else it names MUST be ignored.
- **FR-023**: Existing feature rules still apply after consolidation: at most 100 features per vehicle, 1 to 30 characters per name, no duplicates. When the result would exceed 100, proposed features that do not fit MUST be left out and the user told.
- **FR-024**: Importing for an existing vehicle MUST be available only for a vehicle the signed-in user owns; the vehicle's own features MAY be shown to the reading step so duplicates can be found, and MUST never be shown to anyone else.

### Notes

- The shortcut is requested while the user is working in the form, so reading must finish in a time the user will wait; the time limit and the message shown when it is exceeded are set in the plan.
- A feature's letter case is shared with every user (features are one shared list), so when the sticker's wording differs from an existing shared feature only by letter case, the shared spelling is the one shown.

## Amendment 2 (2026-11-05)

- **FR-036**: The limit of features per vehicle is raised from 20 to 100. This replaces the "no more than 20 features" rule in FR-030 of feature 004 for every way of adding features (typing one in, and the window sticker import). FR-023 and the other feature rules in this document use the new limit. The limit is a single value in the API (`VehicleTagRepository.MaxTagsPerVehicle`), mirrored for the Web form as `StickerImportLimits.MaxFeaturesPerVehicle`, and a test keeps the two equal.

## Amendment 3 (2026-11-05)

### Changes requested

1. **Every feature has a category.** Features are sorted into a fixed set of categories (Safety; Comfort & Convenience; Technology & Audio; Exterior; Interior; Performance & Handling; Wheels & Tires; Towing & Utility; Packages; Other). A feature that has no category yet is categorized automatically by AI, whether it was typed in by hand or imported from a window sticker. A feature that already has a category keeps it.
2. **Categorized display.** The vehicle's Description tab shows its features as one card per category (laid out like a help-center topic list) instead of one long list: the category name and its feature count, the first seven features, and the rest under "Show N more features".
3. **Search the features.** A search box above the cards finds features by name on the vehicle page; only matching features are shown, all of them, grouped by category, with a way to clear the search.

### Requirements

- **FR-037**: The categories MUST be a fixed list shared by every user, and the automatic categorization MUST only be able to choose from it (anything else it answers is ignored). Other is the category of last resort.
- **FR-038**: A category belongs to the shared feature (features are one list shared by every user), so it is decided once and every vehicle that has the feature shows the same category. A feature that already has a category MUST NOT be sent for categorization again or have its category changed.
- **FR-039**: When a feature is added by hand, the category MUST be decided before the page that follows is shown, within the time limit for the reading step. When vehicle details are saved with features imported from a sticker, the new features MUST be categorized in the same save, within the same limit.
- **FR-040**: A failure of the categorization (unavailable, too slow, or an unusable answer) MUST NOT stop a feature from being added or saved. The feature is shown under Other until it is categorized, and a background pass retries features without a category regularly and categorizes features saved before categories existed.
- **FR-041**: Only the feature's name is sent for categorization, never the vehicle, the owner, or any other detail.
- **FR-042**: The Description tab MUST list the vehicle's features in one card per category, in the fixed category order, leaving out categories with no feature, with the count and the first seven features (ordered by name) and the remaining features under a disclosure control that works without a script.
- **FR-043**: The search MUST be a normal page request (it works without a script and the result can be bookmarked), match the feature names ignoring letter case, show every matching feature without folding, say how many matched, say so when none match, and offer to clear the search. The search text is at most as long as a feature name.
- **FR-044**: The edit form's feature list MUST show each feature's category next to its name.
- **FR-045**: The owner cannot change a feature's category in this version.

### Notes

- The categorizer reads the same Azure OpenAI settings as email intake and the sticker import; when they are empty, nothing is categorized and every feature stays under Other.
- Categories are written once, so a wrong category is a known limitation until a way to correct it is specified.