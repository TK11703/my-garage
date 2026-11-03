# My Garage UI Standard

How every page in `MyGarage.Web` looks and behaves. New pages follow this document; the automated test `UiStandardConformanceTests` guards the parts that can be checked in source. Bootstrap 5.3.x is the only UI framework.

## Logo

| Asset | File | Use |
|---|---|---|
| Mark on dark | `src/MyGarage.Web/wwwroot/images/logo-mark-on-dark.svg` | Navigation bar (dark background) |
| Mark on light | `src/MyGarage.Web/wwwroot/images/logo-mark-on-light.svg` | Light surfaces, documents |
| Favicon | `src/MyGarage.Web/wwwroot/images/favicon.svg` | Browser tab; self-contained tile that reads at 16 px |

- The name "My Garage" is always real text next to the mark (never part of the image). The mark image has `alt=""` because the adjacent text names the link.
- Minimum size: 24 px for the mark, 16 px for the favicon.
- Clear space: at least a quarter of the mark's height on every side.
- Do not stretch, rotate, recolor, add effects, or place the dark-background mark on a light surface.

## Palette

| Token | Value | Use |
|---|---|---|
| Garage Blue (`--bs-primary`) | `#0B4F8A` | Primary buttons, links, focus ring |
| Garage Blue hover | `#083A66` | Hover and link hover |
| Night (`--mg-navbar-bg`) | `#0F2A43` | Navigation bar background |
| Page background (`--mg-page-bg`) | `#EEF4FB` | Very light blue behind every page; cards and form controls stay white |
| Amber (`--mg-accent`) | `#F5A623` | Logo accent and avatar; never body text on white |
| Slate (`--mg-secondary`) | `#5C6773` | Secondary (outline) buttons |
| Success, warning, danger, info | Bootstrap defaults | Feedback |

Contrast ratios (WCAG relative luminance): white on Garage Blue 8.40:1; white on Night 14.66:1; Amber on Night 7.23:1; Night on Amber 7.23:1; white on Slate 5.76:1; Garage Blue on white 8.40:1; hover on white 11.62:1. All meet AA (4.5:1 for text, 3:1 for UI components).

Theming is done by overriding Bootstrap 5.3 CSS custom properties in `wwwroot/app.css`; there is no Sass build.

## Typography and spacing

- Page title: the only `h1` (`h2` styling), rendered by `PageHeader`. Sections use `h2` with `h5` styling; sub-sections use `h3`.
- Body text is Bootstrap's default; secondary text uses `text-body-secondary`; small print uses `small`.
- Content gutters come from `container-fluid` (Bootstrap's normal narrow left and right margin); the page frame adds `py-3`. The page header has `mb-3` below it. Use `gap-2` between buttons and `gap-3` between blocks. Readable-width pages (forms, FAQ, About) constrain themselves with `col-lg-8`.

## Page frame

1. Skip link ("Skip to main content") is the first focusable element.
2. `AppNavbar`: fixed top, full width. Signed in: logo and name, search, notifications, help (?), account menu. Signed out: logo and name, help, Sign in.
3. `<main id="main-content" class="container-fluid py-3">` containing the page, which always starts with `PageHeader`.

The search box submits `GET /vehicles?q=` (the existing owner-scoped vehicle search). Notifications is a placeholder menu with no unread indicator. Help links to `/faq` and `/about`, which are viewable without signing in. The account menu shows the user's own name, My Vehicles, Preferences, and Sign out (antiforgery `POST /authentication/logout`).

## Page header: trail and title

`<PageHeader Title="..." Trail="@(new[] { new Crumb("Vehicles") })">` renders "Home" plus the trail, the title, and an optional primary action at the right. The last crumb is the current page (no link). The title also sets the browser tab title to "{Title} – My Garage".

| Page | Route | Trail (after Home) | Title |
|---|---|---|---|
| Home | `/` | none | Welcome, {name} |
| Vehicle list | `/vehicles` | Vehicles | My Vehicles |
| Add vehicle | `/vehicles/new` | Vehicles > New vehicle | Add Vehicle |
| Vehicle detail | `/vehicles/{id}` | Vehicles > {name} | {name} |
| Edit vehicle | `/vehicles/{id}/edit` | Vehicles > {name} > Edit | Edit {name} |
| Vehicle not found | detail or edit | Vehicles | Vehicle not found |
| Add service record | `/vehicles/{id}/service-records/new` | Vehicles > {name} > Add service record | Add service record |
| Service record | `/vehicles/{id}/service-records/{recordId}` | Vehicles > {name} > Service record {date} | Service record {date} |
| Edit service record | `/vehicles/{id}/service-records/{recordId}/edit` | Vehicles > {name} > Service record {date} > Edit | Edit service record |
| Add line item | `.../service-records/{recordId}/line-items/new` | Vehicles > {name} > Service record {date} > Add line item | Add line item |
| Edit line item | `.../line-items/{lineItemId}/edit` | Vehicles > {name} > Service record {date} > Edit line item | Edit line item |
| Service record or line item not found | any of the above | Vehicles | Service record not found / Line item not found |
| Add fuel or charge | `/vehicles/{id}/fuel-energy-entries/new?kind=fuel` or `kind=energy` | Vehicles > {name} > Add fuel / Add charge | Add fuel / Add charge |
| Edit fuel or charging entry | `/vehicles/{id}/fuel-energy-entries/{entryId}/edit` | Vehicles > {name} > Edit fuel entry / Edit charge | Edit fuel entry / Edit charge |
| Add or edit an inspection, registration, insurance entry, or tax | `/vehicles/{id}/{inspections,registrations,insurance,taxes}/new` and `/{entryId}/edit` | Vehicles > {name} > Add inspection / Edit inspection (and likewise for the others) | Same as the last trail item |
| Maintenance entry not found | any edit page above | Vehicles | Entry not found (Vehicle not found on an add page) |
| Preferences | `/preferences` | Preferences | Preferences |
| FAQ | `/faq` | FAQ | Frequently Asked Questions |
| About | `/about` | About | About My Garage |
| API check | `/api-check` | API check | API Check |
| Signed out | `/authentication/signed-out` | Signed out | You have been signed out |
| Sign-in problem | `/authentication/error` | Sign-in problem | Sign-in problem |
| Error | `/Error` | Error | Something went wrong |
| Not found | `/not-found` | Not found | Page not found |

## Component patterns

| Pattern | Standard |
|---|---|
| Primary action | `btn btn-primary`; at most one per page header |
| Secondary action | `btn btn-outline-secondary` (cancel, keep) or `btn btn-outline-primary` (edit, add in a section) |
| Destructive action | `btn btn-outline-danger` to open, `btn btn-danger` to confirm |
| Form buttons | Submit first, then Cancel |
| Row-level buttons | `btn-sm`; page-level buttons regular size |
| Destructive confirmation | Inline `<details>` with a danger alert, a POST form, and buttons "Yes, delete this {vehicle, note, picture}" and "Keep {vehicle, note, picture}". Feature removal is immediately reversible, so it is a one-click remove button with the accessible name "Remove feature {name}" |
| Forms | Bootstrap floating labels (`form-floating`, input before label) on every field; the search boxes keep a visible label and hint. Read-only views use the same grid as the edit form with a small label stacked over each value (`ViewField`), not label/value rows; required fields marked with a visible `*` (`aria-hidden`) and a "* Required" note as the first thing in the form's first card body (never beside the buttons); field errors use `invalid-feedback d-block`; forms limited to `col-lg-8` |
| Cards | Bootstrap card (`border-0 shadow-sm`) with a 16:9 image or placeholder, a bold title link showing "[Year] [Make] [Model]" once, fuel type and transmission badges when set, and a detail line with the mileage and, when a purchase or lease date is set, its year ("Purchased 2022", "Leased 2022", or "Acquired 2022"), each with an icon; long text uses `text-break`. Vehicle cards are `mg-vehicle-card` and lift slightly on hover (not with reduced motion) |
| View page | Page header with an Edit action, then the same tabs read-only (Details, Media, Description, and Maintenance, described below): Details, holding a Basic card (Year, Make, Model, Trim, VIN, Vehicle type, Purchased or leased, Date Purchased or Date Leased, Date lease ends, Mileage on Purchase Date or Lease Date, Latest odometer reading) and a Specifications card (Engine, Horsepower, Drivetrain, Color, Fuel type, Transmission, Seats, Doors, Wheel size, Tire size) as definition lists with "Not set" for empty values, Media as an "Uploaded Images (N)" card with the picture grid in its card body, Description as two equal columns: a Features card (the feature list group in the card body) and a Recent Journal Entries card (the timeline in the card body). The tab panes themselves are not cards; each section is its own card with its heading in the card header. A vehicle has no stored name: it is shown as "[Year] [Make] [Model]" (the year is left out when unknown). A vehicle without a year shows a warning prompt linking to the Details edit tab. Delete vehicle stays below the card |
| Edit page | Page header with a "Back to vehicle" action, then tabs: Details holds one form with a Basic card and a Specifications card (each a card with a card-header naming the group) and Save changes and Cancel below them; Media holds the upload area and the uploaded pictures; Description holds two equal columns: a Features card (Add a feature, then Included Features, in the card body) and, stacked on the right, a Journal card (the new note fields in the card body and the Add note button in the card footer) and a separate Recent Journal Entries card (the timeline in the card body). `?tab=` opens a tab, and a rejected save opens the tab with the first error. Vehicle type, Purchased or leased, Fuel type, Transmission, and Drivetrain are selects starting with "Not set", and a hidden item already on the vehicle stays selected marked "(hidden)" |
| Service record form buttons | The New and Edit service record forms use a primary submit button with a save icon and a Cancel button, each with a `title` explaining the action |`n| Home page | A page header (Welcome, {name}), then a hero card (headline, the product description as the lead, feature badges, the primary actions, and a stats row with the vehicle count when known and "Only you") beside a navy panel holding a decorative car illustration (`aria-hidden`); then an "About My Garage" section of eight icon-and-text feature blurbs in a responsive grid. The emphasized action is Add your first vehicle when the garage is empty, otherwise My vehicles |
| Vehicle form layout | Basic card rows of three: Year, Make, Model; Trim, VIN, Vehicle type; then one row holding Purchased or leased, the date, and Date lease ends; then Mileage and Latest odometer reading on one row (the latter has helper text: "Shown on your garage card unless a newer service record has a higher reading."). The date and mileage labels follow the ownership choice ("Date Purchased" or "Date Leased"; "Mileage on Purchase Date" or "Mileage on Lease Date"; "Date purchased or leased" and "Mileage on Date purchased or leased" while unset). Date lease ends is shown only while Purchased or leased is Leased (`lease-toggle.js`, which also rewrites those labels; the server renders the same state, so it works after a reload or a rejected save, and a lease end is never sent for a vehicle that is not leased). Specifications rows: Engine, Horsepower, Drivetrain; Color, Fuel type, Transmission; Seats, Doors, Wheel size, Tire size |
| Picture upload area | A dashed `mg-dropzone` form with a cloud icon, "Drag & Drop Images Here", and "or click to browse" help text; a visually hidden, full-size file input sits over it so clicking opens the file picker and dropping files uploads them (`dropzone.js`; several files are sent one after another). Without JavaScript a visible Upload button appears. Below it, "Uploaded Images (N)" and a grid of square `mg-image-tile` tiles with a Main picture badge or Make main button, an icon-only delete button (accessible name "Delete picture {name}") that opens an in-tile confirmation, and the caption when there is one |
| Feature box | The Description tab's Features card body starts with an "Add a feature" text box backed by a `<datalist>` filled from `/features/suggest` as the user types (`feature-suggest.js`), followed by "Included Features", a list group with one feature per row and a close button named "Remove feature {name}". Choosing a suggestion adds that shared feature; typing a new name and pressing Add creates a feature every user can then pick. A vehicle can have 20 features |
| Preferences page | Page header, an Account card (display name and email as read-only text), then tabs built with the shared `TabNav` component: Vehicle Types, Fuel Types, Transmissions, Drivetrains. Each tab is a card: the list name in the card header; every item in the card body as a list group with three columns (Name, Item Type "Default" or "Custom", Item Status "Shown" or "Hidden" as a Bootstrap switch that submits on change via `auto-submit.js`, with an Apply button when script is off); and an "Add item" form with a plus icon in the card footer. Hidden items are greyed out, not struck through. `?tab=` opens a tab, and a refused change opens its tab with the message |
| Maintenance tab | The fourth vehicle tab, labeled Maintenance (wrench icon, `?tab=service-records`, pages with `recordsPage`), holds a Bootstrap accordion of three independent items, each its own bordered, rounded block with a gap between them (no `data-bs-parent`, so each opens and closes on its own): Service Records (the header reads "Service Records (N)" with a muted "Last Service {date}" when there are records), Charging and Fueling ("Charging and Fueling (N)" with a muted "Last entry {date}"), and Inspections, Registrations, Insurance, and Taxes (a muted summary of the counts of its lists, for example "2 inspections, 1 registration, 0 insurance, 3 taxes"). `?section=records` (default), `fuel`, or `compliance` decides which item starts open, and the add, edit, and delete pages return to the tab with the item they belong to open; each list pages on its own query value (`recordsPage`, `fuelPage`, `inspectionsPage`, `registrationsPage`, `insurancePage`, `taxesPage`). The Service Records body has an "Add service record" primary button and a list group (not a table) of linked rows, most recent Date Serviced first: date and total cost on the first line, odometer, shop or person ("Not set" when empty), and "N line items" below, then the pager. With no records a compact empty state replaces the list. The edit form does not show this tab |
| Maintenance entry lists | Charging and Fueling and the four lists of the third item (Inspections, Registrations, Insurance, Taxes, each under its own `h3` subheading) share `EntryListBlock`: add actions right-aligned above a `list-group` (never a table) with one row per entry showing every value, a compact empty state naming the kind, and the pager. Each row ends with a row-level `btn-sm btn-outline-secondary` Edit link whose accessible name says what it edits ("Edit fuel from {date}", "Edit inspection from {date}", "Edit registration expiring {date}", "Edit insurance ending {date}", "Edit {type} tax"). Charging and Fueling has two add buttons, "Add fuel" and "Add charge". Amounts use `$` and two decimals; unit prices show two to four decimals |
| Maintenance entry forms | A card on the page frame `MaintenancePage` (`col-lg-10`, `MaintenanceFormCard`): the card body starts with the "* Required" note and holds the form with floating labels, a required `*`, selects that start with a "Choose ..." option, helper text tied to the field with `aria-describedby`, and number inputs with `min`, `max`, and `step` matching the allowed range. The card footer holds Save (save icon; the add pages say "Save", the edit pages "Save changes"), Cancel, and on the edit pages the delete confirmation last (`DeleteEntryPanel`: "Delete this {kind}? This cannot be undone."), each with a title. The fuel and charging kind is fixed when the form opens (no badge on the form) so the labels read "Price per gallon / Total gallons / Total" or "Price per kWh / kWh delivered / Total session cost" |
| Service record page | A "Service record" card (definition list: Date serviced, Odometer reading, Shop name or person), a "Line items" card, and the header actions Back to service records, Edit, and Delete (destructive confirmation: "Delete this service record? This also removes its line items and cannot be undone."). The line items card lists each line in a list group: description and line total, then the pricing as flat cost ("Flat cost $40.00") or labor ("Labor: 1.5 h x $90.00 = $135.00") and one line per part ("2 x Oil filter at $12.50 = $25.00"), with row-level `btn-sm` Edit (accessible name "Edit {description}") and Delete ("Delete {description}") controls, then an "Add line item" button and the bold "Total cost". Amounts are shown with `$` and two decimals |
| Line item form | One card whose body starts with the "* Required" note, then a full-width "What was done" field, a Pricing radio pair ("Flat Cost" / "Labor and Parts"; only the chosen method is saved), and the section for the chosen method (`pricing-toggle.js`; the server renders the same state): Flat Cost has a heading and a blurb explaining why there is a single field, then the flat cost box; Labor and Parts has a heading, Labor hours, Hourly rate, and rows of Part name, Quantity, and Price per part. The card footer holds the "Save Line Item" button (save icon) and Cancel, each with a title. The form starts with three blank part rows (plus existing parts when editing); "Add more part rows" adds three more without saving, up to 25; entirely blank rows are ignored on save. Field and part-row errors ("Part 2: ...") appear next to the field; a full record shows an inline alert |
| Journal timeline | "Recent Journal Entries" is a vertical rail (`mg-timeline`) of entries, newest first: a ring marker, a small muted `yyyy-MM-dd HH:mm:ss UTC` timestamp (with "(edited)" when changed), then the category in bold followed by the note text. On the edit page each entry keeps its Edit and Delete controls |`r`n| Listing page | A search card holding only the search field, a collapsible Filters card whose controls submit with the search form (`form="vehicle-search"`) and whose active criteria are removable badges with a "Clear all" link, then a results card: result count in the header, the card grid (1, 2, 3, then 4 columns), and a footer with "Showing x to y of z entries" and the pager at the right |
| Empty and no-match states | `EmptyState` (full block with an action, or `Compact` inside a section) |
| API failures | `ApiFailureAlert` (warning alert, "Sign in again" when the session is not accepted) |
| Feedback | `alert-success` (`role="status"`), `alert-info`, `alert-warning`, `alert-danger` (`role="alert"`); every alert has a role |

## Accessibility

- Every interactive element is keyboard operable with a visible focus indicator (white outline on the dark bar).
- Icon-only controls carry an accessible name (`aria-label`); icons are `aria-hidden`.
- Motion is disabled for users who prefer reduced motion.
- Pages work from 320 px to 1920 px without horizontal scrolling of primary content.

## Icons

Icons are inline SVGs rendered by `Common/Icon.razor`. The glyph paths come from [Bootstrap Icons](https://icons.getbootstrap.com/) (MIT license, Copyright (c) 2019-2024 The Bootstrap Authors).
