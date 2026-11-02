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
| Forms | Bootstrap floating labels (`form-floating`, input before label) on every field; the search boxes keep a visible label and hint. Read-only views use the same grid as the edit form with a small label stacked over each value (`ViewField`), not label/value rows; required fields marked with a visible `*` (`aria-hidden`) and a "* Required" note; field errors use `invalid-feedback d-block`; forms limited to `col-lg-8` |
| Cards | Bootstrap card (`border-0 shadow-sm`) with a 16:9 image or placeholder, a bold title link showing "[Year] [Make] [Model]" once, fuel type and transmission badges when set, and a detail line with the mileage and, when a purchase or lease date is set, its year ("Purchased 2022", "Leased 2022", or "Acquired 2022"), each with an icon; long text uses `text-break`. Vehicle cards are `mg-vehicle-card` and lift slightly on hover (not with reduced motion) |
| View page | Page header with an Edit action, then the same three tabs read-only: Details, holding a Basic card (Year, Make, Model, Trim, VIN, Vehicle type, Purchased or leased, Date Purchased or Date Leased, Date lease ends, Mileage on Purchase Date or Lease Date) and a Specifications card (Engine, Horsepower, Drivetrain, Color, Fuel type, Transmission, Seats, Doors, Wheel size, Tire size) as definition lists with "Not set" for empty values, Media as a picture grid, Description as two equal columns: Features (Included Features list group), then the journal (Recent Journal Entries timeline). A vehicle has no stored name: it is shown as "[Year] [Make] [Model]" (the year is left out when unknown). A vehicle without a year shows a warning prompt linking to the Details edit tab. Delete vehicle stays below the card |
| Edit page | Page header with a "Back to vehicle" action, then tabs: Details holds one form with a Basic card and a Specifications card (each a card with a card-header naming the group) and Save changes and Cancel below them; Media holds the upload area and the uploaded pictures; Description holds two equal columns: Features (Add a feature, then Included Features) and Journal (new note, then Recent Journal Entries). `?tab=` opens a tab, and a rejected save opens the tab with the first error. Vehicle type, Purchased or leased, Fuel type, Transmission, and Drivetrain are selects starting with "Not set", and a hidden item already on the vehicle stays selected marked "(hidden)" |
| Vehicle form layout | Basic card rows of three: Year, Make, Model; Trim, VIN, Vehicle type; then one row holding Purchased or leased, the date, and Date lease ends; then Mileage on its own row. The date and mileage labels follow the ownership choice ("Date Purchased" or "Date Leased"; "Mileage on Purchase Date" or "Mileage on Lease Date"; "Date purchased or leased" and "Mileage on Date purchased or leased" while unset). Date lease ends is shown only while Purchased or leased is Leased (`lease-toggle.js`, which also rewrites those labels; the server renders the same state, so it works after a reload or a rejected save, and a lease end is never sent for a vehicle that is not leased). Specifications rows: Engine, Horsepower, Drivetrain; Color, Fuel type, Transmission; Seats, Doors, Wheel size, Tire size |
| Picture upload area | A dashed `mg-dropzone` form with a cloud icon, "Drag & Drop Images Here", and "or click to browse" help text; a visually hidden, full-size file input sits over it so clicking opens the file picker and dropping files uploads them (`dropzone.js`; several files are sent one after another). Without JavaScript a visible Upload button appears. Below it, "Uploaded Images (N)" and a grid of square `mg-image-tile` tiles with a Main picture badge or Make main button, an icon-only delete button (accessible name "Delete picture {name}") that opens an in-tile confirmation, and the caption when there is one |
| Feature box | The Description tab's Features column starts with an "Add a feature" text box backed by a `<datalist>` filled from `/features/suggest` as the user types (`feature-suggest.js`), followed by "Included Features", a list group with one feature per row and a close button named "Remove feature {name}". Choosing a suggestion adds that shared feature; typing a new name and pressing Add creates a feature every user can then pick. A vehicle can have 20 features |
| Preferences page | Page header, an Account card (display name and email as read-only text), then tabs built with the shared `TabNav` component: Vehicle Types, Fuel Types, Transmissions, Drivetrains. Each tab is a card: the list name in the card header; every item in the card body as a list group with three columns (Name, Item Type "Default" or "Custom", Item Status "Shown" or "Hidden" as a Bootstrap switch that submits on change via `auto-submit.js`, with an Apply button when script is off); and an "Add item" form with a plus icon in the card footer. Hidden items are greyed out, not struck through. `?tab=` opens a tab, and a refused change opens its tab with the message |
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
