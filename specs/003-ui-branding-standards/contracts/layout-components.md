# Contract: Layout and Shared UI Components

Interfaces that every page depends on. All are static-SSR compatible (no event handlers) and live in `MyGarage.Web`. Related: [web-routes.md](web-routes.md), [../data-model.md](../data-model.md).

## Page frame (`MainLayout`)

Order of elements in the document body:

1. Skip link: `<a class="visually-hidden-focusable" href="#main-content">Skip to main content</a>` (first focusable element).
2. `AppNavbar` (fixed top, full width).
3. `<main id="main-content" class="container-fluid py-3" tabindex="-1">` containing the page (`@Body`). The page's first child is always `PageHeader`.
4. The existing Blazor error UI and reconnect modal.

Layout rules: `body` has top padding equal to the bar height (`--mg-navbar-height`) and `scroll-padding-top` so fixed-bar overlap never hides content or anchor targets. Content uses Bootstrap's normal `container-fluid` gutters and fills the width; readable-width constraints (forms, FAQ, About) are set per page with column classes.

## `AppNavbar`

`<header>` containing `<nav class="navbar navbar-dark fixed-top" aria-label="Main">` with `container-fluid`, background `--mg-navbar-bg`.

| Part | Signed in | Signed out | Markup contract |
|---|---|---|---|
| Brand | yes | yes | `<a class="navbar-brand" href="/">` containing `<img src="images/logo-mark-on-dark.svg" alt="" width="32" height="32">` and the text `My Garage`. The link's accessible name is the visible text; if the image fails, the text remains. |
| Search | yes | no | `<form role="search" method="get" action="/vehicles">` with `<input type="search" name="q" maxlength="100" aria-label="Search vehicles" placeholder="Search vehicles">` and a submit button with a search icon and accessible name "Search". Below `md` it sits in a Bootstrap collapse opened by an icon toggle (`aria-expanded`, `aria-controls`). |
| Notifications | yes | no | Dropdown toggle button (bell icon, accessible name "Notifications", no badge) opening a menu containing the text "You have no notifications." |
| Help | yes | yes | Dropdown toggle button (question-circle icon, accessible name "Help") with items "FAQ" (`/faq`) and "About" (`/about`). |
| Profile | yes | no | Dropdown toggle button showing an initials avatar (or person icon) and, from `sm` up, the display name; accessible name "Account menu for {display name}". Menu: non-interactive header with the display name; item "My vehicles" (`/vehicles`); divider; `<form method="post" action="/authentication/logout">` with `<AntiforgeryToken />` and a `dropdown-item` submit button "Sign out". |
| Sign in | no | yes | `<a class="btn btn-outline-light btn-sm" href="/authentication/login">Sign in</a>` |

Behavior: dropdown/collapse are Bootstrap `data-bs-toggle` controls (keyboard: Enter/Space open, Arrow keys move, Escape closes and returns focus to the toggle). Left-to-right tab order matches the visual order. Visible focus indicator on the dark background (light outline using `--mg-focus-on-dark`). Menus open aligned to the right edge so they never overflow the viewport at 320 px. All motion honors `prefers-reduced-motion`.

## `PageHeader`

```razor
<PageHeader Title="My Vehicles" Trail="@(new[] { new Crumb("Vehicles") })">
    <Actions><a class="btn btn-primary" href="/vehicles/new">Add vehicle</a></Actions>
</PageHeader>
```

| Parameter | Rule |
|---|---|
| `Title` | Required; rendered as the page's only `<h1 class="h2 mb-0">`; also rendered through `<PageTitle>` as `"{Title} – My Garage"`. |
| `Trail` | Crumbs after the implicit "Home". All but the last render as links when `Href` is set; the last renders as plain text with `aria-current="page"`. With an empty trail, "Home" is the current page. |
| `Actions` | Optional; placed right of the title on one row at `sm` and up, wrapping under the title on narrow screens. At most one primary (`btn-primary`) action per page. |

Rendered structure: `<nav aria-label="Breadcrumb"><ol class="breadcrumb">…</ol></nav>` above a flex row with the title and actions, followed by `mb-3` spacing.

### Trail and title rules (FR-034)

| Page | Route | Trail (after Home) | Title | Primary action |
|---|---|---|---|---|
| Home | `/` | (none; "Home" is current) | `Welcome, {display name}` | |
| Vehicle list | `/vehicles` | `Vehicles` | `My Vehicles` | Add vehicle |
| Add vehicle | `/vehicles/new` | `Vehicles` > `New vehicle` | `Add Vehicle` | |
| Vehicle detail | `/vehicles/{id}` | `Vehicles` > `{nickname}` | `{nickname}` | Edit (secondary style) |
| Edit vehicle | `/vehicles/{id}/edit` | `Vehicles` > `{nickname}` (link) > `Edit` | `Edit {nickname}` | |
| Vehicle not found | detail/edit missing | `Vehicles` | `Vehicle not found` | |
| FAQ | `/faq` | `FAQ` | `Frequently Asked Questions` | |
| About | `/about` | `About` | `About My Garage` | |
| API check | `/api-check` | `API check` | `API Check` | |
| Signed out | `/authentication/signed-out` | `Signed out` | `You have been signed out` | |
| Sign-in problem | `/authentication/error` | `Sign-in problem` | `Sign-in problem` | |
| Error | `/Error` | `Error` | `Something went wrong` | |
| Not found | `/not-found` | `Not found` | `Page not found` | |

Vehicle nicknames are user text: always rendered as encoded text and allowed to wrap or truncate with an ellipsis inside the trail (`text-truncate` with a max width) without breaking layout.

## `EmptyState`

Parameters: `Message` (required), `ActionText` and `ActionHref` (optional), `Variant` (`Empty` or `NoMatch`), `Compact` (bool, default false). Default renders a centered block (`text-center py-5`) with a `lead` message and, when supplied, one action link styled `btn btn-primary` (Empty) or `btn btn-outline-primary` (NoMatch). `Compact` renders a single `<p class="text-body-secondary">` for use inside a page section. Used for: empty garage, no vehicles match (default), and empty tag/journal/gallery sections (compact).

## `ApiFailureAlert`

Parameters: `Failure` (`GarageApiFailure`), `ReturnUrl` (app-relative path), `Messages` source (existing `VehicleMessages`/`ApiCheckMessages` strings). Renders `<div class="alert alert-warning" role="alert">` with the message for the failure kind and, when the failure is `NotAccepted`, a "Sign in again" `btn btn-primary` link to `/authentication/login?returnUrl={escaped ReturnUrl}`. Replaces the copy-pasted blocks in the list, detail, create, edit, and API check pages.

## `Icon`

Parameters: `Name` (`Search`, `Bell`, `QuestionCircle`, `PersonCircle`, plus those added for empty states), optional `Size` (default 16). Renders an inline `<svg class="bi" aria-hidden="true" focusable="false" fill="currentColor">`. Icon-only controls MUST carry their own accessible name; the icon is never the only label.

## Standard patterns applied to existing pages

| Pattern | Standard |
|---|---|
| Buttons | Primary action `btn btn-primary`; secondary `btn btn-outline-secondary` (cancel, back) or `btn btn-outline-primary` (edit); destructive `btn btn-danger` / `btn btn-outline-danger`. Form button order: submit (primary) then Cancel. |
| Destructive confirmation | Inline `<details>` disclosure with danger alert text, a POST form, and "Yes, delete …" / "Keep …" buttons; same wording style for vehicles, images, and journal notes. Tag removal is immediately reversible and is the one documented exception (one-click remove button with an accessible name). |
| Forms | `form-label` above control, required marker and help text via `form-text`, validation messages in `invalid-feedback`/`validation-message` styled consistently, constrained to readable width (`col-lg-8`). |
| Cards | Bootstrap card with image/placeholder area, title link, secondary details, actions; long text wraps or truncates without breaking the grid. |
| Feedback | `alert-success` (completed), `alert-info` (information), `alert-warning` (recoverable problem), `alert-danger` (failure/destructive), all with `role="alert"` or `role="status"` as appropriate. |
| Headings | Only `PageHeader` renders `h1`; sections use `h2` (`h5` style), sub-sections `h3`. |
