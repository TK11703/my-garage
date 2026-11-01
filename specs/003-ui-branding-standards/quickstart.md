# Quickstart: Validating UI Branding and Standardization

Run guide for confirming the feature end to end. Contracts: [layout-components.md](contracts/layout-components.md), [web-routes.md](contracts/web-routes.md). Data model: [data-model.md](data-model.md) (no stored data).

## Prerequisites

- .NET 10 SDK (see `global.json`), Docker (Testcontainers and the Aspire-hosted PostgreSQL/Azurite resources), and the Entra setup from feature 001 already completed.
- On branch `003-ui-branding-standards`.

## Build and automated tests

```powershell
dotnet build MyGarage.slnx
dotnet test MyGarage.slnx
```

Expected: zero warnings; all tests pass, including the new `MyGarage.Web.Tests` coverage for the page header, navigation bar (signed-in and signed-out), initials, empty/failure components, anonymous `/faq` and `/about`, and the shared head (favicon link, Bootstrap bundle script).

## Run the app

```powershell
aspire run
```

Open the Web resource from the Aspire dashboard.

## Validation scenarios

### 1. Identity (User Story 1)

1. Open the app signed out (for example `/faq`): header shows the logo and "My Garage", the Help (?) icon, and a Sign in button; no search, bell, or profile menu.
2. Confirm the browser tab shows the My Garage icon and a title like `Frequently Asked Questions – My Garage`.
3. Sign in; select the logo from `/vehicles`: you land on the home page.
4. Block image loading (or rename the logo file temporarily): the text "My Garage" still shows and the layout does not break.

### 2. Navigation bar (User Story 1b)

1. Signed in, scroll a long page (vehicle detail with notes): the bar stays at the top, spans the full width, and nothing is hidden beneath it at the top of the page.
2. Type `civic` in the bar search and press Enter: the browser goes to `/vehicles?q=civic` and the list is filtered to your matching vehicles.
3. Open the bell: a menu says "You have no notifications." and there is no badge.
4. Open the ? menu: links to FAQ and About work.
5. Open the profile menu: your name, "My vehicles", and "Sign out"; Sign out ends the session and returns to the signed-out page.
6. Keyboard only: first Tab stop is "Skip to main content"; Tab/Shift+Tab walk the bar left to right; Enter/Space open menus, Arrow keys move, Escape closes and returns focus.
7. Resize to 320 px wide: no horizontal scrollbar; the search toggle reveals the search box; bell, help, and profile remain reachable.

### 3. Page header and layout (User Story 1c)

| Page | Expected trail | Expected title |
|---|---|---|
| `/` | Home | Welcome, {your name} |
| `/vehicles` | Home / Vehicles | My Vehicles |
| `/vehicles/new` | Home / Vehicles / New vehicle | Add Vehicle |
| `/vehicles/{id}` | Home / Vehicles / {nickname} | {nickname} |
| `/vehicles/{id}/edit` | Home / Vehicles / {nickname} / Edit | Edit {nickname} |
| `/vehicles/{random guid}` | Home / Not found | Page not found (HTTP 404; the status-code page replaces the vehicle page) |

Check on each: earlier crumbs are links, the last crumb is not a link, the "Add vehicle" action sits right of the title, content fills the width inside the standard small side gutters (not a centered column), and a screen reader announces a "Breadcrumb" navigation landmark with the last crumb as the current page.

### 4. Standardization (User Story 2)

1. Walk every page (home, list, new, detail, edit, FAQ, About, API check, signed-out, sign-in problem, not found, error) and confirm only `PageHeader` renders the `h1`, buttons follow the standard (primary, outline secondary, danger), and no page has one-off styles.
2. Trigger each kind of feedback (success, warning, error) and each empty state (empty garage, no search match, empty tags/journal/gallery) and confirm the standard appearance.
3. Delete a vehicle, image, note, and tag: each uses the same confirmation pattern and wording.
4. Compare the palette in the running app against `docs/ui-standard.md`; verify text contrast with a contrast checker (all AA).
5. With the OS "reduce motion" setting on, no non-essential animation runs.
6. Zoom the browser to 200% and 400%: header, bar, and content remain usable.

### 5. Home page (User Story 3)

Signed out, browse to `/`: you are redirected to sign in and return to the home page afterwards. Signed in: a greeting, a short description, and entry points "My vehicles" and "Add vehicle"; with no vehicles, "Add your first vehicle" is emphasized.

## Done when

- Build has zero warnings and all tests pass.
- Scenarios 1 to 5 pass (including keyboard and 320 px checks).
- `docs/ui-standard.md` exists and matches what is implemented.
