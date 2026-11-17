# Feature Specification: UI Redesign (Instrument Cluster Direction)

**Feature Branch**: `016-ui-redesign-instrument-cluster`

**Created**: 2026-11-17

**Status**: Draft

**Input**: User description: "Make the approved 'direction B' mockups real. The UI currently reads as a default Bootstrap admin template. Replace it with a specific, designed product: the instrument-cluster look in `docs/mockups/` built on the tokens in `docs/design-tokens.css`, in light and dark themes, across every page."

Visual references (authoritative for look and layout; this spec is authoritative for behavior):

- Tokens, type roles, contrast rules, migration table: [docs/design-tokens.md](../../docs/design-tokens.md), [tokens.css](../../src/MyGarage.Web/wwwroot/css/tokens.css)
- Screens: [docs/mockups/](../../docs/mockups/) (garage, vehicle, service record, reminders, add vehicle with sticker import, draft review, search, preferences, FAQ, about, signed out, 404)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A consistent, accessible new look in light and dark (Priority: P1)

Every page of My Garage shows the new visual identity: signal-amber on warm-neutral surfaces in the light theme, the same design on near-black surfaces in the dark theme, a display face for titles, a body face for text, and a monospaced face with aligned figures for numbers. The owner can switch between Light and Dark; the choice is remembered in the browser.

**Why this priority**: Everything else builds on it. It also delivers most of the visible change on its own, because every existing page picks up the new colours, type, spacing, and controls without being rearranged.

**Independent Test**: Open any existing page in each theme. Colours, fonts, buttons, inputs, tabs, alerts, and badges follow the new tokens; no page flashes the wrong theme on load; text and controls meet the contrast rules; no font file is requested from another site.

**Acceptance Scenarios**:

1. **Given** a first visit on a device set to dark, **When** any page loads, **Then** it renders in the dark theme with no flash of the light theme.
2. **Given** the owner chooses Light in Preferences, **When** they open any page later in the same browser, **Then** the light theme is used regardless of the device setting.
3. **Given** any page in either theme, **When** text, icons, field borders, and focus indicators are measured, **Then** text pairs reach 4.5:1 and interface parts reach 3:1.
4. **Given** the owner prefers reduced motion, **When** any page loads or is used, **Then** nothing animates.
5. **Given** an amount, odometer reading, date, or VIN, **When** it is shown, **Then** it uses the monospaced face with aligned figures, and amounts are right-aligned in lists and receipts.
6. **Given** a status (overdue, due soon, upcoming, mileage needed, paused, archived), **When** it is shown, **Then** it uses the status colour and treatment defined in the design tokens, and no colour other than status colours is used to mean status.

---

### User Story 2 - The new navigation shell (Priority: P1)

Signed-in pages use a slim left rail instead of the blue top bar, dark sidebar, and second dark title strip. The page title appears large in the page itself, with a small location line above it. Signed-out and public pages (FAQ, About, signed out, sign-in problem) use a light public top bar with a Sign in action.

**Why this priority**: The shell is on every page and is the largest single break from the template look. It must land with the visual foundation so no page shows a mix of old and new frames.

**Independent Test**: Sign in and move through the rail items; each lands on the right page and shows as current. Sign out and open FAQ and About: the public bar appears, with Sign in.

**Acceptance Scenarios**:

1. **Given** a signed-in owner, **When** any page loads, **Then** a rail shows Garage, Add vehicle, Reminders, Drafts (with a count when drafts await review), Search, and Dashboard, with Help and Account at the bottom; the current item is marked and exposed as current to assistive technology.
2. **Given** unread notifications, **When** the rail is shown, **Then** a Notifications item shows an unread dot and opens the same menu of notifications, with the same actions (open a draft, dismiss, mark all read, clear read) as today.
3. **Given** any page, **When** it loads, **Then** it has exactly one level-1 heading (the page title, shown large), a small location line above it, and no separate title bar.
4. **Given** a phone width, **When** the owner opens the menu button, **Then** the rail opens as a drawer over the page, closes with the backdrop or Escape, and no primary content scrolls sideways from 320 px up.
5. **Given** a signed-out visitor, **When** they open FAQ or About, **Then** they see the public top bar with Help links and a Sign in action, and no rail.
6. **Given** keyboard use, **When** the owner tabs from the top of any page, **Then** the first stop is a skip link, every rail item has a visible focus indicator and an accessible name, and icon-only items are named.

---

### User Story 3 - A garage that shows what matters (Priority: P2)

The garage list shows each vehicle as a large photo card with its name on the photo, a status pill, and three readouts: odometer, what is due next, and spend this year. A strip above the list says how many reminders are overdue or due soon and links to them. Archived vehicles sit in their own quiet section instead of mixing into the grid. A vehicle with no picture shows a typographic placeholder instead of a generic car glyph.

**Why this priority**: It is the first thing an owner sees after sign-in and the clearest proof the product is about vehicles rather than rows.

**Independent Test**: With one vehicle that has a due reminder, one with none, and one archived: the strip names the due item, each card shows its own readouts and status, the archived vehicle appears in its own section with a Restore action, and filtering and search behave as before.

**Acceptance Scenarios**:

1. **Given** vehicles with reminders, **When** the garage loads, **Then** the strip states the overdue and due-soon counts with the most urgent item named, and links to Reminders; with none it says nothing needs attention.
2. **Given** an active vehicle, **When** its card is shown, **Then** it shows the picture or placeholder, year and trim line, make and model, a status pill when a reminder is due, odometer, the next due item with remaining miles or days, and spend this year.
3. **Given** a vehicle with no reminders, **When** its card is shown, **Then** the next-due area offers to set one.
4. **Given** an archived vehicle, **When** the list shows all statuses, **Then** it appears below the active vehicles in an Archived section with its archived date, a note that its history is kept, and a Restore action.
5. **Given** the owner searches or filters, **When** results show, **Then** the existing search, filters, status switch, paging, and ownership scoping behave as today.
6. **Given** a vehicle with no picture, **When** its card is shown, **Then** the placeholder shows the year and model and the end of the VIN when known.

---

### User Story 4 - A vehicle page built around the car (Priority: P2)

A vehicle opens on an overview: its photo, name, spec plate, odometer, last service, spend this year, next-due readouts, and needs-attention list. Tabs hold Timeline, Specs, Photos, Reminders, and Documents. The timeline merges services, fuel and charging, compliance records, and journal notes in date order with filters. Missing values read "add" with a way to add them instead of "Not set".

**Why this priority**: The vehicle page is where owners spend their time; today it is a stack of read-only forms with no sense of the car.

**Independent Test**: Open a vehicle with a service record, a fuel entry, a registration, and a note. The overview shows readouts; the timeline lists all four in date order and the filters narrow it; Specs shows every current detail; unset values offer an add action.

**Acceptance Scenarios**:

1. **Given** a vehicle, **When** its page opens, **Then** the overview shows the picture, name, spec plate (engine, power, drivetrain, transmission, VIN), odometer, last service date, spend this year, and any reminders needing attention.
2. **Given** services, fuel or charging, inspections, registrations, insurance, taxes, and journal notes, **When** the owner opens Timeline, **Then** all appear newest first with a kind label, and each filter shows only its kind.
3. **Given** a service in the timeline, **When** the owner opens its receipt link, **Then** they reach the full service record.
4. **Given** a vehicle with unset details, **When** Specs shows, **Then** unset values read "add" and link to editing that detail.
5. **Given** an archived vehicle, **When** its page opens, **Then** it is read-only as today, with Restore offered and a clear archived indication.
6. **Given** a single Add control on the page, **When** the owner opens it, **Then** every record kind that can be added today (service record, fuel, charge, inspection, registration, insurance, tax, reminder, imported record) is reachable from it.

---

### User Story 5 - Records that look like records (Priority: P2)

The service record page reads as a receipt: vehicle context on top, amounts in a right-aligned column, subtotal, tax, and a prominent total. Row actions sit in a small menu on each line. The reminders page ranks reminders by urgency with a progress gauge for each, a status filter, a new-reminder form with a live "next due" preview, and suggestions drawn from the owner's own history.

**Why this priority**: Service records and reminders are the product's core data and its most template-looking screens today.

**Independent Test**: Open a service record with a flat line, a credit, a labor line, and tax: amounts line up, the total equals lines plus tax, and edit or delete is reachable from each line's menu. Open Reminders with one overdue, one due soon, and one paused: they are ranked, filterable, and each shows its remaining distance or time.

**Acceptance Scenarios**:

1. **Given** a service record, **When** it opens, **Then** it shows the vehicle, odometer at service, shop, line items with pricing labels, a right-aligned amount column, subtotal, tax, and total, with credits marked.
2. **Given** a line item, **When** the owner opens its menu, **Then** Edit and Delete (with the existing confirmation) are available, and each action's accessible name says which line it acts on.
3. **Given** reminders of mixed status, **When** the reminders page opens, **Then** they are ordered overdue, due soon, upcoming, mileage needed, paused, with counts on the status filters and a gauge and remaining text on each.
4. **Given** the new-reminder form, **When** an interval and last-done values are entered, **Then** a next-due preview shows the miles and date and notes that whichever comes first applies.
5. **Given** the owner's service history contains a recurring service with no reminder, **When** the reminders page or that service's page is shown, **Then** it suggests a reminder with that service's interval and a one-step way to use it.
6. **Given** a reminder, **When** the owner chooses Mark done, **Then** the existing behavior (date, mileage, next cycle, history entry) is unchanged.

---

### User Story 6 - Importing and reviewing, with the new design (Priority: P3)

Adding a vehicle starts with the window-sticker import as a three-step flow (choose the sticker, reading, review), with the manual form one step away. The draft review page shows the original beside the proposed record, a readiness check as a ring with a field-by-field list, and the same explicit check-then-save behavior as today.

**Why this priority**: These are the product's most distinctive features and deserve a distinctive presentation, but they work today; this restyles and reorders rather than adds capability.

**Independent Test**: Import a sticker: the three steps show in order, values read are marked as coming from the sticker, unclear ones are flagged, proposed features show with what they replace, and nothing is saved until Save. Open a draft with a missing odometer: the check lists it, Save stays unavailable until it is entered and the check is run again.

**Acceptance Scenarios**:

1. **Given** the Add vehicle page, **When** it opens, **Then** the sticker step is first and a visible link offers manual entry.
2. **Given** a sticker is read, **When** the review step shows, **Then** each filled value is marked as read from the sticker, unclear values are flagged, notices are listed, and proposed features show their replaces notes and can be unticked.
3. **Given** a draft, **When** the review page opens, **Then** the original (file or email details) and the proposal sit side by side on a wide screen and stacked on a phone.
4. **Given** the compatibility check, **When** it runs, **Then** the ring and list show each checked field as OK, missing, or invalid in words, and Save is unavailable until the check passes.
5. **Given** a file without script support, **When** the page is used, **Then** every step still works through form submits as today.

---

### User Story 7 - Search, preferences, help, and the error pages (Priority: P3)

Search shows one large field, filters, active-filter chips, and results that show where each match was found with the match highlighted. Preferences keeps the account details, the Light and Dark choice, and the four lists, with a switch for each item. FAQ and About use the public shell with the new layout. Signed out, sign-in problem, and Not found pages follow the new look; the Not found page uses an odometer that rolls to 404.

**Why this priority**: Complete, but low risk and lower traffic.

**Independent Test**: Search for a word that matches a feature and a colour: both fields are shown and highlighted. Switch an item off in Preferences and see it hidden in forms. Open a missing address: the Not found page appears with the odometer.

**Acceptance Scenarios**:

1. **Given** a search term, **When** results show, **Then** each result lists the fields that matched with the term highlighted, and active filters appear as removable chips.
2. **Given** Preferences, **When** the owner switches a list item off or on, **Then** it behaves as today (hidden items leave forms) with a clear Shown or Hidden state.
3. **Given** the owner chooses a theme in Preferences, **When** they leave and return, **Then** it is kept for this browser.
4. **Given** FAQ, **When** the owner types a keyword, **Then** matching questions are filtered, highlighted, and opened.
5. **Given** an unknown address, **When** it is opened, **Then** the Not found page shows with the existing wording and a way home and to search.

---

### Edge Cases

- A vehicle name of 60+ characters, or with no year, wraps or truncates without breaking the card, header, or rail.
- A vehicle with no picture, no VIN, no reminders, and no records still looks complete and offers "add" actions.
- 320 px width, 200% zoom, and 1920 px width: no primary content scrolls sideways; the rail becomes a drawer on small screens.
- Windows forced-colors mode: borders, focus, and status remain distinguishable.
- Scripts disabled or slow: every page remains usable (the app is statically rendered; interactions without script fall back to form submits as today).
- A custom font file fails to load: the fallback stack is used with no layout break.
- Very large figures (odometer over 999,999, totals over $99,999) fit in their readouts.
- An owner with zero vehicles sees an empty state with the add and sticker-import actions, not an empty grid.
- Archived vehicles: reminders stay disabled and history read-only exactly as in the archive feature.

## Requirements *(mandatory)*

### Functional Requirements

**Visual foundation**

- **FR-001**: The system MUST apply the colour, type, spacing, radius, and motion tokens in `docs/design-tokens.css` to every page, in a light and a dark theme that differ only in colour values.
- **FR-002**: No component or page MUST hard-code a colour outside the token set (documents shown as paper excepted).
- **FR-003**: The three font families MUST be served from the application itself; no page MUST request fonts from another site; a fallback stack MUST be used until or if a font fails.
- **FR-004**: Text MUST meet 4.5:1 contrast and interface parts (including form-field edges and focus indicators) MUST meet 3:1, in both themes.
- **FR-005**: The theme MUST be Light or Dark only; first visit follows the device setting; the owner's choice MUST be kept in the browser only and MUST apply before first paint.
- **FR-006**: All motion MUST be disabled when the owner prefers reduced motion.
- **FR-007**: Numbers (odometer, money, dates, VIN) MUST use the monospaced face with aligned figures; amounts in lists and receipts MUST be right-aligned.
- **FR-008**: Status colours MUST be used only for status and MUST map as defined in the design tokens document.

**Shell and navigation**

- **FR-010**: Signed-in pages MUST use a left rail in place of the top bar, sidebar, and title strip, with the items listed in User Story 2 and an accessible name for every item.
- **FR-011**: The Notifications menu MUST remain available to signed-in owners with its existing contents and actions, reachable from the rail, with an unread indicator.
- **FR-012**: Every page MUST have one level-1 heading, shown large in the page, and a small location line above it; the separate page bar MUST be removed.
- **FR-013**: Public pages MUST use a public top bar with Help links, theme control, and a Sign in action; the rail MUST NOT appear for signed-out visitors.
- **FR-014**: On a phone, the rail MUST open as a drawer from a menu button and close by backdrop or Escape.
- **FR-015**: Existing routes and their authorization MUST be preserved; the garage list MUST also be served at the home address.

**Garage and vehicle**

- **FR-020**: The garage list MUST show, for each active vehicle, picture or typographic placeholder, name, status pill when a reminder needs attention, odometer, next due with remaining distance or time, spend this year, and fuel, transmission, and drivetrain labels when set.
- **FR-021**: The garage MUST show an attention strip with overdue and due-soon counts linking to Reminders.
- **FR-022**: Archived vehicles MUST appear in their own section with archived date, a history-kept note, and Restore.
- **FR-023**: The vehicle page MUST open on an overview and offer Timeline, Specs, Photos, Reminders, and Documents tabs, a single Add control, and the existing Edit, Archive or Restore, and Delete actions with their confirmations.
- **FR-024**: Timeline MUST merge services, fuel and charging entries, inspections, registrations, insurance, taxes, and journal notes newest first, with filters by kind and a link from each service to its record.
- **FR-025**: Unset vehicle details MUST read as an add action instead of "Not set".
- **FR-026**: The latest odometer reading shown on a vehicle MUST be the same value everywhere it appears (list card, overview, specs).

**Records, reminders, import, review**

- **FR-030**: The service record page MUST present the receipt layout described in User Story 5 and the existing line-item rules (flat or labor and parts, credits, tax, total).
- **FR-031**: The reminders page MUST rank by urgency, show status filters with counts, a gauge per reminder, a new-reminder form with a live next-due preview, and suggestions from the owner's history.
- **FR-032**: The Add vehicle page MUST present sticker import as sticker, reading, and review steps with manual entry one step away, preserving the existing import behavior, limits, and notices.
- **FR-033**: The draft review page MUST present original and proposal side by side on wide screens, a readiness ring with field list, and the existing check-before-save behavior.
- **FR-034**: Search results MUST show the fields that matched with the term highlighted and active filters as removable chips.
- **FR-035**: Preferences MUST show account details, the theme choice, and the four lists with a Shown or Hidden switch per item.
- **FR-036**: FAQ and About MUST follow the public layout and keep their existing content; the FAQ keyword filter MUST highlight matches.
- **FR-037**: The Not found page MUST show the odometer treatment with the existing wording and actions.

**Preserved behavior**

- **FR-040**: Every existing capability, authorization rule, validation rule, and data rule MUST be preserved; this feature changes presentation and information layout only, plus the read-only figures in FR-020, FR-021, and FR-024.
- **FR-041**: Every existing control that has an accessible name today MUST keep one; icon-only controls MUST be named.
- **FR-042**: The UI standard document MUST be replaced by one that matches the new design, and the automated conformance checks MUST be updated to guard it.

### Key Entities *(include if feature involves data)*

- **Vehicle summary (read model)**: the existing garage card data plus a next-due summary (reminder name, status, remaining miles and days) and spend this year. Read-only; no new stored data.
- **Vehicle timeline entry (read model)**: a dated, typed, owner-scoped entry (service, fuel or charging, inspection, registration, insurance, tax, journal) with a headline, amount when it has one, and a link target. Read-only; no new stored data.
- **Suggested reminder (read model)**: a recurring service found in the owner's records with a proposed interval. Read-only; saved only through the existing reminder form.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On every page in both themes, 100% of text pairs reach 4.5:1 and 100% of interface-part pairs reach 3:1, measured by the design token preview and a page-level check.
- **SC-002**: No page makes a request to a font or style host other than the application's own; the three font families together add no more than 250 KB over the wire on first load.
- **SC-003**: A first-time reviewer shown a screenshot of any page with the logo removed can say it is a vehicle-tracking product, and none of the review's template symptoms (default system font, stock blue/grey admin shell, four-tile counts row, generic placeholder car icon, stock hero with eight-icon feature grid) remain.
- **SC-004**: An owner can see what is overdue or due soon across all vehicles within one page load of signing in, without opening a vehicle.
- **SC-005**: An owner can read a service record's total and verify it against its lines at a glance: all amounts align on one right edge and the total is the largest figure on the page.
- **SC-006**: All existing user journeys (add, edit, archive, restore, delete a vehicle; add and edit every record kind; import a sticker; review a draft; mark a reminder done; search; change preferences) still complete, verified by the existing automated tests updated for the new markup.
- **SC-007**: No page scrolls sideways at 320 px, and all pages are usable with keyboard only and with scripts disabled.
- **SC-008**: The solution builds with zero warnings and all tests pass.

## Assumptions

- The mockups in `docs/mockups/` and the tokens in `docs/design-tokens.css` are approved (decisions recorded in `docs/design-tokens.md`: brownish light-mode accent text, 14 px body text, self-hosted fonts, Light and Dark only).
- Bootstrap 5.3.x stays the only UI framework; the redesign is expressed through token overrides, component CSS, and markup changes, not a new framework.
- The application remains statically rendered; interactions use small page scripts and form submits as today.
- The mockups' sample values (reminder dates, journal notes, features, intake address) are illustrative and are not product content.
- The default status filter on the garage list stays as it is today (all vehicles), not the mockup's Active default.
- The read-only figures in FR-020, FR-021, and FR-024 need small additions to what the API returns; they add no stored data, no schema change, and no new write path.
- The Dashboard page stays at its current address and is reachable from the rail; its counts row is replaced by the new treatment.
- Delivery is in stages (foundation and shell first). Pages not yet restyled keep working on the new foundation in the meantime.
