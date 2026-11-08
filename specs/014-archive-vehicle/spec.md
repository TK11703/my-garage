# Feature Specification: Archive Vehicle

**Feature Branch**: `014-archive-vehicle`

**Created**: 2026-11-07

**Status**: Draft

**Input**: User description: "In the application I need to be able to archive a car, in addition to deleting it. I may not want to get rid of the history, because I sold or got rid of the car. An archive capability would permit me to keep it, but remove it from any measured metrics."

**Refinement (2026-11-08, planning input)**: Active and archived vehicles are told apart by a filter on the vehicle list rather than by a separate archive page. Archiving disables every reminder on the vehicle (tax due, registration due, and the rest) without deleting any, and a disabled reminder is never counted or listed as something that needs attention. Archived vehicles stay in the fuel and charging calculations (such as the monthly spend chart), because that money was really spent; every other garage-wide figure leaves them out.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Archive a vehicle I no longer own and keep its history (Priority: P1)

A signed-in user who has sold or otherwise gotten rid of a vehicle chooses to archive it instead of deleting it. After confirming, the vehicle leaves the user's active garage and stops counting toward the garage's figures (fuel and charging history is the one exception, see Story 2), but everything recorded for it (details, service records, maintenance entries, journal notes, images, documents, reminders) is kept exactly as it was and can still be opened and read later.

**Why this priority**: This is the whole point of the feature. Today the only way to get a sold vehicle out of the garage is to delete it, which destroys its history. Archiving removes the need to choose between a cluttered, distorted garage and losing the records.

**Independent Test**: With a vehicle that has service records, fuel entries, a reminder, a journal note, and an image, archive it and confirm it disappears from the active garage and from every dashboard figure, then open it from the Archived view and confirm every item is still there unchanged.

**Acceptance Scenarios**:

1. **Given** a user viewing one of their active vehicles, **When** they choose to archive it, **Then** they are asked to confirm, and on confirmation the vehicle is marked archived with the date it was archived and no longer appears in their active garage.
2. **Given** a user who starts to archive a vehicle, **When** they cancel the confirmation, **Then** the vehicle remains active and unchanged.
3. **Given** a vehicle that has been archived, **When** the user opens it from the Archived view, **Then** all of its details, service records, maintenance entries, journal notes, images, documents, and reminders are shown exactly as they were before archiving.
4. **Given** a vehicle that is already archived, **When** the user views it, **Then** the archive action is not offered again and the vehicle is clearly labeled as archived with its archive date.
5. **Given** two users, **When** one archives a vehicle, **Then** nothing changes for the other user, and the other user cannot archive, view, or restore that vehicle.

---

### User Story 2 - Archived vehicles drop out of the garage figures, except fuel and charging (Priority: P1)

Once a vehicle is archived, the garage figures stop counting it: the number of vehicles, the number of service records, the reminders that need attention (and the counts of overdue and due-soon reminders), the garage-wide reminders view, and the recent activity list behave as if the archived vehicle were not part of the garage. The one exception is fuel and charging: the archived vehicle's fuel and charging entries still count in the entry count and still appear in the monthly spend chart and in any other fuel or charging calculation, because that money was really spent and removing it would rewrite the user's spending history.

**Why this priority**: The user archives a vehicle to stop a car that is gone from distorting the garage and raising reminders, while keeping what it cost to run. Without this, archiving would be just a hiding place, and sold vehicles would keep inflating totals and raising reminders.

**Independent Test**: Note every dashboard figure, the spend chart, and the reminders and activity lists for a garage with two vehicles; archive one; confirm the vehicle count, service record count, attention list, overdue and due-soon counts, and recent activity lose that vehicle's contribution, while the fuel and charging entry count and every month of the spend chart are unchanged.

**Acceptance Scenarios**:

1. **Given** a garage with archived and active vehicles, **When** the user opens the dashboard, **Then** the vehicle count and the service record count include only active vehicles.
2. **Given** an archived vehicle that had fuel or charging entries, **When** the user views the dashboard, **Then** its entries are still included in the fuel and charging entry count and in the spend chart for every month, and the chart identifies that vehicle as archived.
3. **Given** an archived vehicle that had overdue or due-soon reminders, **When** the user views the dashboard or the garage-wide reminders view, **Then** its reminders are not listed and are not counted as overdue or due soon.
4. **Given** an archived vehicle with journal notes, **When** the user views recent activity, **Then** none of its entries are listed.
5. **Given** a garage whose only vehicle is archived, **When** the user opens the dashboard, **Then** the vehicle count, service record count, reminder counts, and attention list show zero or are empty and the page invites them to add a vehicle, while the fuel and charging figures still reflect the archived vehicle's history.
6. **Given** a vehicle that is archived, **When** the user restores it, **Then** its records and reminders count again in every figure and list.

---

### User Story 3 - Filter the vehicle list between active and archived vehicles (Priority: P2)

The vehicle list (and the vehicle search that shares its filters) offers a status filter with three choices: Active, Archived, and All. Active is the default, so a user who never archives anything sees no difference and an archived vehicle never clutters the everyday garage. Choosing Archived lets the user look up the history of a car they once owned (for example to answer a buyer's or insurer's question, or to recall what was spent on it). Each archived vehicle is marked as archived on its card and shows the date it was archived, and opens the same read-only vehicle view.

**Why this priority**: Keeping history has value only if it can be found again, and a filter on the list the user already uses is the lightest way to do it.

**Independent Test**: Archive two of three vehicles; confirm the list shows only the active one by default; switch the filter to Archived and confirm only the other two appear with their archive dates; switch to All and confirm all three appear; open an archived one to read its history.

**Acceptance Scenarios**:

1. **Given** a user with active and archived vehicles, **When** they open the vehicle list without choosing a status, **Then** only active vehicles are shown.
2. **Given** the vehicle list, **When** the user chooses Archived, **Then** they see only their own archived vehicles, each labeled archived with its archive date, and the choice is kept in the page address so it can be bookmarked and survives paging.
3. **Given** the vehicle list, **When** the user chooses All, **Then** active and archived vehicles are listed together and each archived one is labeled.
4. **Given** a user with no archived vehicles, **When** they choose Archived, **Then** they see a message explaining what archiving does rather than an empty page.
5. **Given** a search or other filter is applied, **When** the user changes the status filter, **Then** the other criteria are kept.
6. **Given** an archived vehicle opened from the list, **When** the user reads its records, **Then** nothing about it can be changed except restoring or deleting it.

---

### User Story 4 - Restore an archived vehicle (Priority: P2)

If a vehicle was archived by mistake, or the user acquires it again, they can restore it from its page (found with the Archived filter). It returns to the active garage with all its history intact, counts in the metrics again, its reminders are enabled again, and it can be edited and added to as before.

**Why this priority**: Archiving must be safely reversible; otherwise users would hesitate to use it, and a mistaken archive would force them to recreate a vehicle by hand.

**Independent Test**: Archive a vehicle, restore it, and confirm it appears in the active garage with all its records and that dashboard figures return to their earlier values.

**Acceptance Scenarios**:

1. **Given** an archived vehicle, **When** the user chooses to restore it (with a confirmation), **Then** it returns to the active garage immediately, the archive date is cleared, and all its history is unchanged.
2. **Given** a restored vehicle, **When** the user adds a service record or edits its details, **Then** the change is accepted as for any active vehicle.
3. **Given** a restored vehicle with reminders, **When** the user views reminders, **Then** they are enabled again and their statuses are worked out from the current date and the vehicle's current mileage, as for any active vehicle; a reminder the user had paused themselves before archiving is still paused.

---

### User Story 5 - Archived vehicles stay out of the way of new work (Priority: P3)

Archived vehicles are not offered when the user is choosing a vehicle for something new: reviewing an emailed or uploaded maintenance draft, adding a reminder, or any similar picker. This keeps new entries from being attached to a car the user no longer owns by accident. Archived vehicles also cannot be added to or edited until restored.

**Why this priority**: It protects archived vehicles from accidental changes and keeps pickers short, but the feature is already useful without it.

**Independent Test**: With one active and one archived vehicle, open the review of a maintenance draft and confirm only the active vehicle can be chosen; try to add a service record to the archived vehicle and confirm it is not possible.

**Acceptance Scenarios**:

1. **Given** a maintenance draft awaiting review, **When** the user chooses which of their vehicles it belongs to, **Then** only active vehicles are offered.
2. **Given** a draft proposed for a vehicle that has since been archived, **When** the user opens it, **Then** it is shown as needing a vehicle, the archived vehicle is not preselected, and the user can choose an active vehicle or discard the draft.
3. **Given** an archived vehicle, **When** the user or anyone else tries to add, edit, or remove any of its records, notes, images, documents, or reminders, or to change its details, **Then** the change is refused and nothing is altered.

---

### User Story 6 - Delete remains available, for active and archived vehicles (Priority: P3)

Deleting a vehicle works as it does today (permanent removal with all attached data after explicit confirmation). The user can delete an active vehicle or one that is archived, so an archived vehicle that is no longer worth keeping can eventually be removed for good. The confirmation makes clear that deletion cannot be undone and, for an active vehicle, mentions archiving as the way to keep the history.

**Why this priority**: The request is to archive "in addition to deleting", so deletion must remain; it is lower priority because it already exists.

**Independent Test**: Delete an archived vehicle and confirm it and all its data are gone; open the delete confirmation for an active vehicle and confirm it points out archiving as an alternative.

**Acceptance Scenarios**:

1. **Given** an archived vehicle, **When** the user deletes it and confirms, **Then** the vehicle and everything attached to it are permanently removed and it no longer appears in any list.
2. **Given** an active vehicle, **When** the user starts to delete it, **Then** the confirmation states that deletion is permanent and that archiving keeps the history.
3. **Given** the delete confirmation, **When** the user cancels, **Then** the vehicle is kept unchanged.

---

### Edge Cases

- A user opens or acts on a vehicle in another browser tab after it was archived or restored elsewhere: the application shows its current state, and an action that no longer applies (for example editing a vehicle that has just been archived) is refused with a clear message rather than silently failing.
- A user tries to archive, restore, or view an archived vehicle that belongs to someone else, by guessing its address: it behaves as if the vehicle does not exist and reveals nothing about it.
- A user archives a vehicle that has reminders (for example tax due or registration due): every reminder is kept but disabled, none is counted or listed as needing attention, no new notice is raised for it, and notices already waiting for it are cleared; when the vehicle is restored, the reminders are enabled again with their due points unchanged.
- A user archives a vehicle that has drafts proposed for it, or an emailed document names it: the vehicle is no longer suggested, and a draft already proposed for it is shown as needing a vehicle.
- A user archives their last active vehicle: the default (Active) vehicle list shows its empty state with an invitation to add a vehicle and a way to switch to the Archived filter.
- A user archives a vehicle whose identifying details (for example VIN or name) match another active vehicle: archiving and restoring do not merge or alter either vehicle.
- A user with a large number of archived vehicles chooses the Archived filter: the list is paged like any other and loads in a reasonable time.
- A vehicle is archived while a file upload or draft review involving it is in progress: the in-progress action does not attach new records to the archived vehicle.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to archive any of their active vehicles after explicit confirmation, and the system MUST record the date the vehicle was archived.
- **FR-002**: Archiving MUST keep every item attached to the vehicle (details, tags, service records, maintenance entries, journal notes, images, documents, and reminders) unchanged and retrievable.
- **FR-003**: An archived vehicle MUST NOT appear in the default (Active) vehicle list or in any place that offers vehicles for selection for new work (including the choice of vehicle when reviewing a maintenance draft).
- **FR-004**: An archived vehicle and everything attached to it MUST be excluded from every garage-wide measurement and summary, including the counts of vehicles and service records, the reminders needing attention and their overdue and due-soon counts, the garage-wide reminders view, and the recent activity list, with this one exception: fuel and charging entries of archived vehicles MUST remain in the fuel and charging entry count, the fuel and charging spend chart (for all months), and any other fuel or charging calculation, and the chart MUST identify an archived vehicle as archived. Any garage-wide metric added in the future MUST follow the same rule (fuel and charging calculations keep archived vehicles; everything else leaves them out) unless its spec says otherwise.
- **FR-005**: Archiving MUST disable every reminder on the vehicle (including tax-due and registration-due reminders) without deleting any: a disabled reminder MUST NOT become overdue or due soon, MUST NOT appear in or be counted by any garage-wide figure or list, MUST NOT raise any notice, and MUST show as disabled because its vehicle is archived. Notices already waiting for the vehicle's reminders MUST be cleared on archiving. Restoring MUST enable them again with their due points, history, and any individual pause the user had set unchanged.
- **FR-006**: The vehicle list and vehicle search MUST offer a status filter with the choices Active (the default), Archived, and All; the choice MUST combine with every other search and filter, MUST be kept in the page address, and MUST survive paging. Each archived vehicle in a list MUST show that it is archived and the date it was archived, and MUST open to read all its history.
- **FR-007**: An archived vehicle MUST be read-only: the system MUST refuse any attempt to change its details or to add, edit, or remove its records, entries, notes, tags, images, documents, or reminders, and MUST offer only restore and delete as actions on it.
- **FR-008**: Users MUST be able to restore an archived vehicle, which returns it to the active garage with all its history intact, clears its archive date, enables its reminders again, and makes it count in all measurements again.
- **FR-009**: Users MUST be able to delete a vehicle whether it is active or archived, with the same explicit confirmation and the same permanent removal of all attached data as today; the confirmation for an active vehicle MUST tell the user that archiving keeps the history.
- **FR-010**: The system MUST only reveal, archive, restore, or delete a vehicle for the user who owns it; a request for another user's vehicle MUST be answered as "not found" and MUST NOT disclose that it exists. Ownership MUST come from the authenticated user, never from the request.
- **FR-011**: A maintenance draft proposed for a vehicle that is archived MUST be treated as having no vehicle chosen, and including a draft MUST NOT be possible into an archived vehicle.
- **FR-012**: Archiving and restoring MUST be attributable to the authenticated user who performed them, and MUST NOT expose sensitive vehicle or record data in logs.
- **FR-013**: Every archived vehicle MUST be clearly labeled as archived, with its archive date, wherever it is shown.
- **FR-014**: The change MUST apply to vehicles that already exist: all existing vehicles MUST start as active with no loss of data.

### Key Entities *(include if feature involves data)*

- **Vehicle**: An existing entity owned by one user. It gains an archived state: either active, or archived with the date it was archived. Archived vehicles keep every attached item and remain owned by the same user.
- **Reminder**: An existing entity belonging to one vehicle. It gains no new stored state of its own; whether it is enabled follows its vehicle's state, so archiving disables all of a vehicle's reminders and restoring enables them, independent of any pause the user set on an individual reminder.
- **Garage metrics**: Any figure, chart, or list that summarizes the user's garage as a whole (dashboard counts, spend chart, reminders needing attention, recent activity, and the garage-wide reminders view). They are derived from active vehicles, except fuel and charging figures, which also include archived vehicles.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can archive a vehicle in under 30 seconds from its detail page, including confirmation.
- **SC-002**: After archiving, 100% of the vehicle count, service record count, reminder counts, attention entries, and recent activity entries contributed by that vehicle disappear, and the remaining values match a manual count of the user's active vehicles in all tested garages, including a garage with no active vehicles; the fuel and charging entry count and every month of the spend chart are unchanged by archiving.
- **SC-008**: After archiving a vehicle with reminders, 100% of its reminders still exist and show as disabled, none is counted as overdue or due soon, and none raises a notice, and after restoring every one is enabled again with unchanged due points.
- **SC-003**: 100% of a vehicle's history (records, entries, notes, images, documents, and reminders) is present and unchanged after archiving and again after restoring.
- **SC-004**: A user can find an archived vehicle with the status filter and open its history in under 30 seconds from the vehicle list.
- **SC-005**: In testing with at least two different users, 100% of attempts by one user to view, archive, restore, or delete another user's vehicle are refused without revealing that it exists.
- **SC-006**: 100% of attempts to change an archived vehicle or add anything to it are refused, and 0 archived vehicles are offered when choosing a vehicle for new work.
- **SC-007**: After restoring a vehicle, all garage figures return to the values they had before it was archived (assuming no other changes).

## Assumptions

- Archiving is a user action only; vehicles are never archived automatically, and no reason or note is required (the archive date is recorded automatically). Capturing why or how the vehicle left the garage (sold, scrapped, sale price) is out of scope.
- "Measured metrics" means every garage-wide figure, chart, or list in the application today (listed in FR-004) and any added later; per-vehicle views of an archived vehicle show its own history as it was. Fuel and charging entries are the exception and stay in the fuel and charging calculations; the entry count follows the same rule so the figures never disagree. The count of drafts awaiting review is unaffected, because a draft belongs to the user rather than to a vehicle.
- Archived vehicles are read-only until restored; this keeps archived vehicles a trustworthy record. A user who needs to add a final record for a sold vehicle restores it, adds the record, and archives it again.
- Reminders on an archived vehicle are disabled rather than deleted, so restoring does not require recreating them. Records such as registrations, inspections, insurance, and taxes carry dates but raise no notices of their own today, so reminders are the only due-date items to disable.
- Existing delete behavior is unchanged (permanent, with all attached data, after confirmation) and is now also available for archived vehicles.
- Archived vehicles are reached through the status filter on the existing vehicle list and search, which uses the application's existing look and layout; the exact placement of the filter is a design decision for planning.
- There is no limit on the number of archived vehicles, and archived vehicles continue to be private to their owner exactly as active vehicles are.
