# Contract: Web Routes (Expanded Vehicle Details and Lookup Preferences)

All routes require a signed-in user (the existing fallback authorization policy). Pages are static SSR (`[ExcludeFromInteractiveRouting]`) and call the API with the user's token through typed API clients. Mutations are antiforgery-protected form posts followed by a redirect (post-redirect-get). Changes to the vehicle routes from [feature 002's web routes](../../002-vehicle-tracking/contracts/web-routes.md) are listed here; everything not mentioned is unchanged.

## New page

| Route | Purpose | Query | Notes |
|---|---|---|---|
| `/preferences` | Account details and lookup management | `tab` (`vehicle-types` default, `fuel-types`, `transmissions`, `drivetrains`); an unknown value opens the first tab | Page header trail Home > Preferences. Account details card: display name and email (read-only, from the current-user endpoint; "Not available" if the call fails, with the rest of the page usable). Tabbed card below with the four lookups. Signed-out requests redirect to sign-in and return here. |

### Lookup tab

Each tab shows a list (one row per item, Bootstrap list group, not a table) with: the item name; a badge "Default" or "Yours"; a badge "Hidden" for hidden items (hidden rows are visually muted); and a button "Hide" or "Show" (its accessible name includes the item name). Below the list is an add form: a labeled text input (max 30 characters) and an "Add" button. The empty-after-hiding state is not an error: the list still shows every item as hidden.

| Form name | Result |
|---|---|
| `add-lookup-{kind}` (the add form inside the tab's mapping scope `lookup-{kind}`; the `<form>` has `data-testid="add-lookup-{kind}"`) | POST to API `addLookupItem`; success redirects to `/preferences?tab={kind}`; 400 and 409 re-render the same tab with the message next to the field (409 distinguishes "That item already exists.", "That item already exists but is hidden. Show it instead.", and "You can add up to 50 items to this list."). |
| `toggle-{itemId}` (inside the tab's mapping scope; `data-testid="toggle-{kind}-{itemId}"`) | `setLookupItemVisibility` with the inverse of the current state; redirects back to the same tab. A 404 (item vanished or not the caller's) shows a neutral "That item could not be found" message on that tab. |

## Changed: account menu

`AppNavbar` account dropdown becomes: display name (text), "Preferences" (`/preferences`), divider, "Sign out" (the "My vehicles" item was removed by Amendment 1; the brand link and vehicle search reach the garage). The navbar search, help menu, and notifications are unchanged.

## Changed: vehicle pages

| Route | Change |
|---|---|
| `/vehicles` | Cards show `year make model` (year omitted when absent). The garage page currently exposes only the search box (the API, `VehicleListCriteria`, and the filter-options client already support the other filters), so this feature adds a collapsible "Filters" panel (open when a filter is active) holding the existing selects for Make, Color, Doors, Tag and number inputs for Minimum and Maximum mileage, plus new selects for Vehicle Type, Fuel Type, Transmission, and Drivetrain (choices from the filter-options endpoint) and number inputs for Minimum year and Maximum year. New query keys: `minYear`, `maxYear`, `vehicleTypeId`, `fuelTypeId`, `transmissionId`, `drivetrainId`; active criteria appear as removable badges like the existing ones; min greater than max year shows an inline message. Search text also matches year, VIN, and engine. |
| `/vehicles/new` | Form with tabs: Basic Info and Specifications (below). Year is required. On success redirects to the new vehicle. A rejected save opens the tab that holds the first error. |
| `/vehicles/{id:guid}` | Basic Info and Specifications tabs show the fields below as definition lists with "Not set" for empty values; Vehicle Type, Fuel Type, Transmission, and Drivetrain show the item name. A vehicle with no year shows a prompt with a link to `/vehicles/{id}/edit?tab=basic`. Media and Description tabs are unchanged. |
| `/vehicles/{id:guid}/edit` | Same tabs and fields as add. Selects offer the user's shown items plus, if hidden, the vehicle's current item labeled "{name} (hidden)". |

### Field placement (add, edit, and detail)

| Tab | Fields in order |
|---|---|
| Basic Info | Rows: Year*, Make*, Model*; Trim, VIN, Vehicle Type (select); Purchased or Leased (select: not set, Purchased, Leased), Date Purchased / Date Leased (date; label follows the select), Date Lease Ends (date, shown only while Leased) on one row; Mileage (mi; "Mileage on Purchase Date" / "Mileage on Lease Date") |
| Specifications | Rows: Engine, Horsepower, Drivetrain (select); Color, Fuel Type (select), Transmission (select); Seats, Doors, Wheel size (in), Tire size |
| Media | Click-or-drop upload area, then "Uploaded Images (N)" tiles (Amendment 1) |
| Description | Two equal columns: Features ("Add a feature", then "Included Features"; shared, auto-completed) and Journal (new note, then "Recent Journal Entries" timeline) (Amendments 1 and 2) |

`*` required. Each select starts with a "Not set" option. Selected values are posted as lookup item ids.

## Navigation

The `?tab=` values for vehicles stay `basic`, `specifications`, `media`, `description`. The shared tab strip (`Common/TabNav`) renders icon + label tabs with an underline on the active tab (`mg-tabs`, as in `docs/ui-standard.md`), uses Bootstrap tab data attributes (no new JavaScript), exposes `role="tablist"`/`tab`/`tabpanel` with `aria-controls` and `aria-selected`, and wraps/scrolls within its container on narrow screens.

## Accessibility and presentation

Bootstrap 5.3 components only; labels on every input and select; validation messages are linked with `aria-describedby`; the Hide/Show buttons have names such as "Hide Sedan"; user-provided text (item names, vehicle values) is rendered encoded (no `MarkupString`); icon-only controls have accessible names; animations respect `prefers-reduced-motion`.

## Amendment 1 (2026-11-02)

- There is no Name field and no `#nickname` input; the page title, breadcrumb, and delete text use "[Year] [Make] [Model]".
- `GET /features/suggest?query=text` (signed-in only, `Cache-Control: no-store`): returns a JSON array of at most 10 shared feature names containing the text, from API `GET /api/features`; a blank query or an API failure returns `[]`.
- Picture upload: `POST /vehicles/{id}/images` is unchanged (one `file` per post); the page script posts several dropped files one after another and then shows the last result. The caption input was removed from the upload form.
- Three small module scripts (`wwwroot/js/lease-toggle.js`, `dropzone.js`, `feature-suggest.js`) are loaded from `App.razor`; each works through delegated events, so enhanced navigation needs no re-initialization, and every behavior still works without them (server-rendered lease state, an Upload button, typing a feature and pressing Add).
