---

description: "Task list for UI Branding and Standardization"
---

# Tasks: UI Branding and Standardization

**Input**: Design documents from `/specs/003-ui-branding-standards/`

**Prerequisites**: plan.md, spec.md, research.md (R1-R15), data-model.md, contracts/ (`layout-components.md`, `web-routes.md`), quickstart.md

**Tests**: Included. The constitution (VIII) requires test-first development: write each phase's tests, run them, and confirm they FAIL before implementing. This feature has no database or API changes, so tests are bUnit component tests and `WebAppFactory` page tests in `tests/MyGarage.Web.Tests`. Visual properties (contrast, overlap, responsiveness, keyboard flow) are verified manually via quickstart.md.

**Organization**: Tasks are grouped by user story. Story labels match spec.md: US1 recognizable identity (P1), US1b persistent navigation bar (P1), US1c page header (P1), US2 consistent look and feel (P2), US3 enriched home page (P3). All work is in `MyGarage.Web` and `MyGarage.Web.Tests` (plus `docs/`); no API, database, DTO, package, or project changes.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US1b, US1c, US2, US3, only on user-story phase tasks
- Constraints from data-model.md, contracts/, and research.md are quoted in the task that creates the artifact; do not loosen them
- All components must be static-SSR compatible: no `@onclick` or other Blazor event handlers in layout or header components (research R1)

## Phase 1: Setup

**Purpose**: Confirm a clean baseline before changing anything.

- [X] T001 Run `dotnet build MyGarage.slnx` and `dotnet test MyGarage.slnx` on branch `003-ui-branding-standards` and confirm zero warnings and all tests pass (Docker required); fix nothing here, only record that the baseline is green
- [X] T002 [P] Confirm `tests/MyGarage.Web.Tests/MyGarage.Web.Tests.csproj` references `bunit` (already pinned in `Directory.Packages.props`) and `Microsoft.AspNetCore.Mvc.Testing`; reuse the existing helpers `tests/MyGarage.Web.Tests/Common/WebAppFactory.cs`, `tests/MyGarage.Web.Tests/Common/TestAuthHandler.cs`, and the bUnit setup in `tests/MyGarage.Web.Tests/Vehicles/VehicleTestContext.cs`; add a missing reference only if absent

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Small shared building blocks that the navigation bar and every page depend on: `Crumb`, `PageHeader`, `Icon`, and the initials helper.

**CRITICAL**: No user story work begins until this phase is complete.

### Tests first (confirm they fail)

- [X] T003 [P] `ClaimsPrincipalExtensionsTests` in `tests/MyGarage.Web.Tests/Common/ClaimsPrincipalExtensionsTests.cs` for `GetInitials()`: "Ada Lovelace" -> `AL`; "Mary Jane Watson" -> `MW` (first and last word only); "prince" -> `P`; extra whitespace is ignored; lowercase input is uppercased; "ada@example.com" (preferred_username fallback) -> `A`; no name claims (neutral name `Signed-in user`) -> empty string; result is never longer than two characters; leading non-letter characters such as "(admin) Bob" are skipped
- [X] T004 [P] `IconTests` in `tests/MyGarage.Web.Tests/Common/IconTests.cs`: each `IconName` (`Search`, `Bell`, `QuestionCircle`, `PersonCircle`) renders one `<svg>` with `class="bi"`, `aria-hidden="true"`, `focusable="false"`, `fill="currentColor"`, a `viewBox`, and at least one `<path>`; default `width`/`height` is 16; the `Size` parameter changes both; the icon never renders text content
- [X] T005 [P] `PageHeaderTests` in `tests/MyGarage.Web.Tests/Components/Layout/PageHeaderTests.cs`: with an empty trail the markup is `<nav aria-label="Breadcrumb">` containing an `<ol class="breadcrumb">` with a single "Home" item that is NOT a link and has `aria-current="page"`; with trail `[Crumb("Vehicles")]` "Home" is a link to `/` and "Vehicles" is the last item (plain text, `aria-current="page"`); with trail `[Crumb("Vehicles","/vehicles"), Crumb("Civic")]` "Vehicles" is a link to `/vehicles` and only "Civic" is current; an `Href` on the last crumb is ignored; exactly one `<h1>` with the title text; the `Actions` fragment renders inside the title row after the `<h1>`; a label containing `<script>` is rendered encoded; long crumb labels carry `text-truncate`; a blank `Title` or blank crumb `Label` throws `ArgumentException`

### Implementation

- [X] T006 [P] Create `src/MyGarage.Web/Components/Layout/Crumb.cs`: `public sealed record Crumb(string Label, string? Href = null)` with `Label` "Required, non-empty" (throw `ArgumentException` when null or whitespace); `Href` "Optional app-relative path" (reject values that do not start with `/` or that start with `//`)
- [X] T007 [P] Add `GetInitials(this ClaimsPrincipal user)` to `src/MyGarage.Web/Common/ClaimsPrincipalExtensions.cs`: derive from `GetDisplayName()`; "Up to two uppercase letters from the first letters of the first and last name words; empty for the neutral name" (`NeutralName`); makes T003 pass
- [X] T008 [P] Create `src/MyGarage.Web/Common/Icon.razor` with an `IconName` enum (`Search`, `Bell`, `QuestionCircle`, `PersonCircle`) and parameters `Name` (required) and `Size` (default 16): renders an inline `<svg class="bi" aria-hidden="true" focusable="false" fill="currentColor" viewBox="0 0 16 16">`; take the path data from the corresponding Bootstrap Icons (MIT) 16x16 glyphs (`search`, `bell`, `question-circle`, `person-circle`); makes T004 pass
- [X] T009 Create `src/MyGarage.Web/Components/Layout/PageHeader.razor` per `contracts/layout-components.md`: parameters `Title` (required), `Trail` (`IReadOnlyList<Crumb>`, default empty), `Actions` (`RenderFragment?`); renders `<PageTitle>@Title – My Garage</PageTitle>` (en dash), `<nav aria-label="Breadcrumb"><ol class="breadcrumb mb-1">` with the implicit "Home" crumb (link to `/`) followed by `Trail`; all but the last crumb are links when `Href` is set, the last is plain text with `aria-current="page"` and `Href` ignored; then `<div class="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-3">` containing `<h1 class="h2 mb-0">@Title</h1>` and the `Actions` fragment; crumb labels get `text-truncate` with `max-width` styling via a CSS class (no inline styles); makes T005 pass

**Checkpoint**: Foundation ready. User stories can begin.

---

## Phase 3: User Story 1 - Recognizable site identity (Priority: P1)

**Goal**: The My Garage logo, name, favicon, brand palette, and tab icon exist and are applied consistently, including on anonymous pages.

**Independent Test**: Open the application signed out and signed in on several pages and confirm the logo, name, and tab icon are present and consistent, selecting the logo goes home, and the name stays visible when the logo image fails to load.

### Tests first (confirm they fail)

- [X] T010 [P] [US1] `BrandAssetTests` in `tests/MyGarage.Web.Tests/Common/BrandAssetTests.cs` using `WebAppFactory` with an anonymous client: `GET /images/logo-mark-on-dark.svg`, `/images/logo-mark-on-light.svg`, and `/images/favicon.svg` each return 200 with content type `image/svg+xml`; each body is well-formed XML whose root is `svg` with a `viewBox`, is under 5,120 bytes, contains no `<script>`, no `on*` event attributes, and no external `href`/`xlink:href` references
- [X] T011 [P] [US1] `SharedHeadTests` in `tests/MyGarage.Web.Tests/Components/SharedHeadTests.cs` using `WebAppFactory` with an anonymous client against `/authentication/signed-out`: the document `<head>` contains `<link rel="icon" type="image/svg+xml" href="...">` whose href (including any fingerprint) resolves with 200, and `<meta name="theme-color" content="#0F2A43">`
- [X] T012 [P] [US1] `BrandLinkTests` in `tests/MyGarage.Web.Tests/Components/Layout/BrandLinkTests.cs`: renders `<a class="navbar-brand" href="/">` containing an `<img>` whose `src` ends with the on-dark logo file name, `alt=""`, `width="32"`, `height="32"`, followed by the text "My Garage" as real text (not part of the image); the link's accessible text is "My Garage"

### Implementation

- [X] T013 [P] [US1] Create `src/MyGarage.Web/wwwroot/images/logo-mark-on-dark.svg`: 64x64 `viewBox`, transparent background, white garage-roofline outline (stroke, no fill) with an amber `#F5A623` headlight bar beneath it; hand-authored, original, no text, no script, no external references, under 5 KB; used in the dark bar
- [X] T014 [P] [US1] Create `src/MyGarage.Web/wwwroot/images/logo-mark-on-light.svg`: same geometry as T013 with the roofline in Garage Blue `#0B4F8A` and the amber `#F5A623` bar, transparent background
- [X] T015 [P] [US1] Create `src/MyGarage.Web/wwwroot/images/favicon.svg`: compact icon form, 64x64 `viewBox`, a rounded square tile (`rx` 12) filled Garage Blue `#0B4F8A` with a simplified thick white roofline and amber `#F5A623` bar so it reads at 16 px on any tab background
- [X] T016 [US1] Add the brand tokens to `src/MyGarage.Web/wwwroot/app.css` per research R2/R3: `:root` overrides for `--bs-primary` `#0B4F8A` and `--bs-primary-rgb`, `--bs-link-color`/`--bs-link-color-rgb` `#0B4F8A`, `--bs-link-hover-color`/`--bs-link-hover-color-rgb` `#083A66`, `--bs-secondary-color` kept, `--bs-focus-ring-color` derived from the primary; component-level `.btn-primary`, `.btn-outline-primary`, and `.btn-outline-secondary` variable overrides (`--bs-btn-bg`, `--bs-btn-border-color`, `--bs-btn-hover-bg`, `--bs-btn-hover-border-color`, `--bs-btn-active-bg`, `--bs-btn-active-border-color`, `--bs-btn-disabled-*`) so primary buttons use `#0B4F8A` with hover `#083A66`; slate secondary `#5C6773` (e.g. `--mg-secondary`); brand-only tokens `--mg-navbar-bg: #0F2A43`, `--mg-accent: #F5A623` (logo and highlights only, never body text on white), `--mg-focus-on-dark` (light focus outline for dark surfaces), and `--mg-navbar-height: 3.5rem`; success, warning, danger, and info keep Bootstrap defaults; no Sass, no other framework
- [X] T017 [US1] Update `src/MyGarage.Web/Components/App.razor` `<head>`: add `<link rel="icon" type="image/svg+xml" href="@Assets["images/favicon.svg"]" />` and `<meta name="theme-color" content="#0F2A43" />` (makes T011 pass); keep the existing stylesheet links
- [X] T018 [US1] Create `src/MyGarage.Web/Components/Layout/BrandLink.razor` (anchor `navbar-brand`, `href="/"`, `<img src="@Assets["images/logo-mark-on-dark.svg"]" alt="" width="32" height="32">` then the text "My Garage" in a `<span>`); in `src/MyGarage.Web/Components/Layout/MainLayout.razor` replace the existing `<a class="navbar-brand" href="/">My Garage</a>` with `<BrandLink />` so the logo is visible now (this header is replaced in US1b); makes T010-T012 pass
- [X] T019 [US1] Verify contrast of the palette with a contrast checker and record the computed ratios for `docs/ui-standard.md` (T058): white on `#0B4F8A` >= 4.5:1, white on `#0F2A43` >= 4.5:1, `#F5A623` on `#0F2A43` >= 3:1, white on `#5C6773` >= 4.5:1, `#0B4F8A` and `#083A66` link text on white >= 4.5:1; adjust the `app.css` tokens if any ratio fails

**Checkpoint**: The logo, favicon, tab icon, and palette are live on every page, including anonymous ones.

---

## Phase 4: User Story 1b - Persistent top navigation bar (Priority: P1)

**Goal**: Every page has a fixed, full-width bar with brand, search, notifications, help, and profile (signed in) or brand, help, and Sign in (signed out); content sits beneath it in a full-width area with standard gutters; FAQ and About pages exist.

**Independent Test**: Sign in, open several pages, scroll each, and confirm the bar stays at the top and spans the width, search navigates to the filtered vehicle list, the bell/help/profile menus open and behave per spec, Sign out works, the bar works at 320 px and by keyboard, and `/faq` and `/about` open without signing in.

### Tests first (confirm they fail)

- [X] T020 [P] [US1b] `AppNavbarSignedInTests` in `tests/MyGarage.Web.Tests/Components/Layout/AppNavbarTests.cs` (signed-in principal with name claim "Ada Lovelace"): `<nav class="... fixed-top ..." aria-label="Main">` containing a `container-fluid`; parts appear in this order: brand (link to `/`), search form, notifications toggle, help toggle, profile toggle; search form is `method="get"` `action="/vehicles"` `role="search"` with `<input type="search" name="q" maxlength="100" aria-label="Search vehicles">` and a submit button with accessible name "Search"; the search sits inside a Bootstrap collapse container with a toggle button that has `data-bs-toggle="collapse"`, `aria-controls`, `aria-expanded="false"` and accessible name "Toggle search"; notifications toggle has accessible name "Notifications", `data-bs-toggle="dropdown"`, no badge element, and its menu contains the text "You have no notifications."; help toggle has accessible name "Help" and a menu with links "FAQ" -> `/faq` and "About" -> `/about`; profile toggle has accessible name "Account menu for Ada Lovelace", shows initials "AL", and its menu shows the full name, a "My vehicles" link to `/vehicles`, a divider, and a `<form method="post" action="/authentication/logout">` containing an antiforgery token field and a submit button "Sign out"; a display name containing `<b>x</b>` is rendered encoded
- [X] T021 [P] [US1b] `AppNavbarSignedOutTests` in `tests/MyGarage.Web.Tests/Components/Layout/AppNavbarSignedOutTests.cs` (anonymous principal): contains the brand link, the help toggle with FAQ/About items, and a link "Sign in" to `/authentication/login`; contains NO search form, NO notifications toggle, NO profile toggle, and no logout form
- [X] T022 [P] [US1b] `MainLayoutTests` in `tests/MyGarage.Web.Tests/Components/Layout/MainLayoutTests.cs`: the first focusable element is the skip link `<a href="#main-content">Skip to main content</a>`; the navbar precedes `<main id="main-content" tabindex="-1">`; `<main>` has class `container-fluid` and NOT `container`; the body content (`@Body`) renders inside `<main>`; the Blazor error UI element is still present
- [X] T023 [P] [US1b] Extend `tests/MyGarage.Web.Tests/Components/SharedHeadTests.cs` (created in T011): the document contains a `<script defer src="...bootstrap.bundle.min.js">` that appears before the `_framework/blazor.web.js` script, and `lib/bootstrap/dist/js/bootstrap.bundle.min.js` resolves with 200 anonymously
- [X] T024 [P] [US1b] `FaqPageTests` and `AboutPageTests` in `tests/MyGarage.Web.Tests/Help/FaqPageTests.cs` and `tests/MyGarage.Web.Tests/Help/AboutPageTests.cs` using `WebAppFactory`: anonymous `GET /faq` and `GET /about` return 200 (no redirect to sign-in); each page has exactly one `<h1>` ("Frequently Asked Questions" / "About My Garage"), a breadcrumb "Home / FAQ" or "Home / About" with the last crumb current, a `<title>` of "Frequently Asked Questions – My Garage" / "About My Garage – My Garage"; anonymous responses show the signed-out bar (Sign in link, no profile toggle); signed-in responses show the profile toggle

### Implementation

- [X] T025 [US1b] Create `src/MyGarage.Web/Components/Layout/AppNavbar.razor` per `contracts/layout-components.md`: `<header>` with `<nav class="navbar navbar-dark fixed-top" aria-label="Main">` using `container-fluid` and the `--mg-navbar-bg` background (via CSS class, no inline styles); `[CascadingParameter] Task<AuthenticationState>` to decide signed-in vs signed-out; `<BrandLink />`; signed-in: search collapse container (`navbar-collapse`-style `collapse` below `md`, always visible from `md` up) with toggle button (`<Icon Name="IconName.Search" />`, label "Toggle search"), search form with `name="q"` `maxlength="100"`, then Notifications, Help, Profile dropdowns (Bootstrap `data-bs-toggle="dropdown"`, `dropdown-menu dropdown-menu-end`, `aria-expanded="false"`); profile toggle shows an initials avatar circle (`GetInitials()`, or `<Icon Name="IconName.PersonCircle" />` when empty) plus the display name from `sm` up (`d-none d-sm-inline`), accessible name `Account menu for {display name}`; Sign out is `<form method="post" action="/authentication/logout"><AntiforgeryToken /><button type="submit" class="dropdown-item">Sign out</button></form>`; signed-out: brand, Help dropdown, and `<a class="btn btn-outline-light btn-sm" href="/authentication/login">Sign in</a>`; no event handlers; makes T020 and T021 pass
- [X] T026 [US1b] Rewrite `src/MyGarage.Web/Components/Layout/MainLayout.razor`: skip link first (`<a class="visually-hidden-focusable" href="#main-content">Skip to main content</a>`), `<AppNavbar />`, `<main id="main-content" class="container-fluid py-3" tabindex="-1">@Body</main>`, then the existing `#blazor-error-ui` block; remove the old `<header class="navbar bg-body-tertiary ...">`, `<NavMenu />`, and `<LoginDisplay />`; makes T022 pass
- [X] T027 [US1b] Add navigation styles in `src/MyGarage.Web/wwwroot/app.css`: `body { padding-top: var(--mg-navbar-height); }` and `html { scroll-padding-top: var(--mg-navbar-height); }`; navbar background `var(--mg-navbar-bg)` and minimum height `var(--mg-navbar-height)`; avatar circle (about 2rem, `--mg-accent` background, dark text meeting 4.5:1); search input styled for the dark bar; visible focus outline using `--mg-focus-on-dark` on bar controls; `@media (prefers-reduced-motion: reduce)` disabling transitions on `.collapse`, `.dropdown-menu`, and `.navbar` elements; ensure no horizontal scrolling at 320 px (dropdown menus end-aligned)
- [X] T028 [US1b] Reference Bootstrap's JavaScript in `src/MyGarage.Web/Components/App.razor`: add `<script defer src="@Assets["lib/bootstrap/dist/js/bootstrap.bundle.min.js"]"></script>` immediately before the `_framework/blazor.web.js` script (makes T023 pass); no custom JavaScript is written
- [X] T029 [P] [US1b] Create `src/MyGarage.Web/Help/Faq.razor` (`@page "/faq"`, `@attribute [AllowAnonymous]`) using `<PageHeader Title="Frequently Asked Questions" Trail="@(new[] { new Crumb("FAQ") })" />` and a Bootstrap `accordion` (native buttons with `data-bs-toggle="collapse"`, headings as `h2`) with short starter answers on: adding a vehicle, editing or deleting a vehicle, tags, journal notes and categories, pictures (up to 10, 5 MB each), searching and filtering, and who can see your data (only you); general product text only, no user data; limit text width with column classes (`col-lg-8`)
- [X] T030 [P] [US1b] Create `src/MyGarage.Web/Help/About.razor` (`@page "/about"`, `@attribute [AllowAnonymous]`) using `<PageHeader Title="About My Garage" Trail="@(new[] { new Crumb("About") })" />` with a short description of what My Garage does (private list of your vehicles, details, tags, journal notes, pictures), constrained with `col-lg-8`; makes T024 pass together with T029
- [X] T031 [US1b] Delete `src/MyGarage.Web/Components/Layout/NavMenu.razor`, `src/MyGarage.Web/Components/Layout/LoginDisplay.razor`, and `tests/MyGarage.Web.Tests/Components/Layout/LoginDisplayTests.cs`; search the repository (`grep` for `NavMenu`, `LoginDisplay`, `navbar-nav`) and remove any remaining references; delete the now-unused `.navbar` rules from `src/MyGarage.Web/Components/Layout/MainLayout.razor.css` if any (keep the `#blazor-error-ui` styles)

**Checkpoint**: The branded bar works for signed-in and signed-out visitors; FAQ and About open from the help menu.

---

## Phase 5: User Story 1c - Page header with breadcrumb trail and title (Priority: P1)

**Goal**: Every page begins with the standard `PageHeader` using the trail and title rules in `contracts/layout-components.md`; no page renders its own `h1` or ad-hoc breadcrumb.

**Independent Test**: Visit the home page, vehicle list, a vehicle detail page, its edit page, and the not-found, error, and authentication pages and confirm each shows the specified trail and title, crumb links navigate correctly, and the tab title is "[Title] – My Garage".

### Tests first (confirm they fail)

- [X] T032 [P] [US1c] Update `tests/MyGarage.Web.Tests/Vehicles/VehicleListTests.cs`: breadcrumb "Home / Vehicles" with "Vehicles" current, `<h1>` "My Vehicles", the `data-testid="add-vehicle"` link ("Add vehicle" to `/vehicles/new`) inside the page header title row; no second `<h1>`
- [X] T033 [P] [US1c] Update `tests/MyGarage.Web.Tests/Vehicles/VehicleDetailPageTests.cs`: trail "Home / Vehicles / {nickname}" with `Vehicles` linking to `/vehicles` and the nickname current; `<h1>` is the nickname; the `data-testid="edit-vehicle"` link sits in the header actions; the old "← My garage" back link is gone; not-found case: HTTP 404, `<h1>` "Vehicle not found", trail "Home / Vehicles"; API-failure case: `<h1>` "Vehicle", trail "Home / Vehicles"; a nickname containing markup is encoded
- [X] T034 [P] [US1c] Update `tests/MyGarage.Web.Tests/Vehicles/VehicleEditPageTests.cs` and `tests/MyGarage.Web.Tests/Vehicles/VehicleCreatePageTests.cs`: edit trail "Home / Vehicles / {nickname} / Edit" with the nickname linking to `/vehicles/{id}`, `<h1>` "Edit {nickname}" (the stored nickname, not an unsaved posted value); edit not-found: 404, `<h1>` "Vehicle not found"; create trail "Home / Vehicles / New vehicle", `<h1>` "Add Vehicle"
- [X] T035 [P] [US1c] Update `tests/MyGarage.Web.Tests/Home/HomeTests.cs`, `tests/MyGarage.Web.Tests/ApiCheck/ApiCheckPageTests.cs`, `tests/MyGarage.Web.Tests/Authentication/SignedOutPageTests.cs`, and `tests/MyGarage.Web.Tests/Authentication/AuthenticationErrorPageTests.cs`: home trail is just "Home" (current) with `<h1>` "Welcome, {display name}"; API check trail "Home / API check", `<h1>` "API Check"; signed-out trail "Home / Signed out", `<h1>` "You have been signed out"; sign-in problem trail "Home / Sign-in problem", `<h1>` "Sign-in problem"; each has exactly one `<h1>`
- [X] T036 [P] [US1c] Add `tests/MyGarage.Web.Tests/Components/Pages/ErrorAndNotFoundPageTests.cs` using `WebAppFactory`: an unknown anonymous or signed-in route re-executes to the not-found page rendering inside the standard frame with trail "Home / Not found" and `<h1>` "Page not found"; the `/Error` page renders trail "Home / Error" and `<h1>` "Something went wrong"; both include the navigation bar and the skip link

### Implementation

- [X] T037 [P] [US1c] Adopt `PageHeader` in `src/MyGarage.Web/Vehicles/VehicleList.razor`: `Title="My Vehicles"`, `Trail=[Crumb("Vehicles")]`, `Actions` = the existing "Add vehicle" `btn btn-primary` link with `data-testid="add-vehicle"`; remove the hand-written `<h1 class="h3 mb-0">` row and the `<PageTitle>`
- [X] T038 [P] [US1c] Adopt `PageHeader` in `src/MyGarage.Web/Vehicles/VehicleDetail.razor`: success state `Title=@vehicle.Nickname`, `Trail=[Crumb("Vehicles","/vehicles"), Crumb(vehicle.Nickname)]`, `Actions` = the Edit link (`btn btn-outline-primary`, `data-testid="edit-vehicle"`); not-found state `Title="Vehicle not found"`, `Trail=[Crumb("Vehicles")]` (keep the 404 status code); failure state `Title="Vehicle"`, `Trail=[Crumb("Vehicles")]`; remove the old `<nav aria-label="Breadcrumb">` back link, the in-page `<h1>`, the `<p>` make/model stays below the header as a lead line, and the `<PageTitle>`
- [X] T039 [P] [US1c] Adopt `PageHeader` in `src/MyGarage.Web/Vehicles/VehicleEdit.razor`: capture the stored nickname when the vehicle loads (not from a posted `Model`); `Title="Edit {storedNickname}"`, `Trail=[Crumb("Vehicles","/vehicles"), Crumb(storedNickname, "/vehicles/{VehicleId}"), Crumb("Edit")]`; not-found `Title="Vehicle not found"` with `Trail=[Crumb("Vehicles")]`; failure `Title="Edit Vehicle"` with `Trail=[Crumb("Vehicles")]`; remove the `<h1>` and `<PageTitle>`
- [X] T040 [P] [US1c] Adopt `PageHeader` in `src/MyGarage.Web/Vehicles/VehicleCreate.razor`: `Title="Add Vehicle"`, `Trail=[Crumb("Vehicles","/vehicles"), Crumb("New vehicle")]`; remove the `<h1>` and `<PageTitle>`
- [X] T041 [P] [US1c] Adopt `PageHeader` in `src/MyGarage.Web/Home/Home.razor`: `Title="Welcome, {displayName}"`, empty trail; remove the `<h1>` and `<PageTitle>`; keep the current content for now (enriched in US3)
- [X] T042 [P] [US1c] Adopt `PageHeader` in `src/MyGarage.Web/ApiCheck/ApiCheck.razor` (`Title="API Check"`, `Trail=[Crumb("API check")]`), `src/MyGarage.Web/Authentication/SignedOut.razor` (`Title="You have been signed out"`, `Trail=[Crumb("Signed out")]`), and `src/MyGarage.Web/Authentication/AuthenticationError.razor` (`Title="Sign-in problem"`, `Trail=[Crumb("Sign-in problem")]`); remove each page's `<h1>` and `<PageTitle>`
- [X] T043 [P] [US1c] Adopt `PageHeader` in `src/MyGarage.Web/Components/Pages/NotFound.razor` (`Title="Page not found"`, `Trail=[Crumb("Not found")]`; replace the `<h3>`) and `src/MyGarage.Web/Components/Pages/Error.razor` (`Title="Something went wrong"`, `Trail=[Crumb("Error")]`; replace `<h1 class="text-danger">`/`<h2 class="text-danger">`, keep the Request ID block, and demote the "Development Mode" `<h3>` to a small section using `h2` styling so the page has one `<h1>`); makes T036 pass
- [X] T044 [US1c] Run the Web tests (`dotnet test tests/MyGarage.Web.Tests`) and fix any remaining assertions that referenced the removed headings or the old header; confirm `Routes.razor` `FocusOnNavigate Selector="h1"` still focuses the page title (no change expected)

**Checkpoint**: Every existing page uses the standard header; US1, US1b, and US1c are all deliverable together as the branded frame.

---

## Phase 6: User Story 2 - Consistent look and feel across all pages (Priority: P2)

**Goal**: Buttons, forms, cards, empty states, feedback messages, and destructive confirmations follow one documented standard, with no one-off styles, and the standard is written down.

**Independent Test**: Walk every page and compare headings, buttons, forms, cards, empty states, alerts, and delete confirmations against `docs/ui-standard.md`; the conformance test passes and no page uses a one-off style.

### Tests first (confirm they fail)

- [X] T045 [P] [US2] `EmptyStateTests` in `tests/MyGarage.Web.Tests/Common/EmptyStateTests.cs`: default `Empty` renders `text-center py-5` with a `lead` message and an action link `btn btn-primary` when `ActionText`/`ActionHref` are supplied; `NoMatch` renders the action as `btn btn-outline-primary`; no action link when none supplied; `Compact` renders a single `<p class="text-body-secondary">` and no `lead`; message text is encoded
- [X] T046 [P] [US2] `ApiFailureAlertTests` in `tests/MyGarage.Web.Tests/Common/ApiFailureAlertTests.cs`: `NotAccepted` renders `<div class="alert alert-warning" role="alert">` with the supplied not-accepted message and a `btn btn-primary` link "Sign in again" to `/authentication/login?returnUrl={escaped ReturnUrl}`; `Unavailable` renders the unavailable message and no sign-in link; the return URL is escaped (for example `/vehicles/{id}/edit` -> `%2Fvehicles%2F...`)
- [X] T047 [P] [US2] `UiStandardConformanceTests` in `tests/MyGarage.Web.Tests/Common/UiStandardConformanceTests.cs` scanning every `src/MyGarage.Web/**/*.razor` file (locate the repo root from the test assembly path): no `<h1` outside `Components/Layout/PageHeader.razor`; no inline `style=` attributes; no solid `btn-secondary` (only `btn-outline-secondary` is allowed); no `class="container"` (only `container-fluid`); every `alert alert-*` element carries `role="alert"` or `role="status"`; no `text-danger small` field error blocks (field errors use `invalid-feedback d-block`)
- [X] T048 [P] [US2] Update the vehicle component tests to the standard markup and wording: `tests/MyGarage.Web.Tests/Vehicles/VehicleTagsSectionTests.cs` (empty state is the compact `text-body-secondary` paragraph; add-tag errors render in an `invalid-feedback d-block` element with `role="alert"` and the field's `aria-describedby`), `tests/MyGarage.Web.Tests/Vehicles/VehicleJournalSectionTests.cs` and `tests/MyGarage.Web.Tests/Vehicles/VehicleGallerySectionTests.cs` (compact empty state; delete confirmation buttons read "Yes, delete this note"/"Keep note" and "Yes, delete this picture"/"Keep picture"), `tests/MyGarage.Web.Tests/Vehicles/VehicleDetailPageTests.cs` (vehicle delete confirmation reads "Yes, delete this vehicle"/"Keep vehicle"), `tests/MyGarage.Web.Tests/Vehicles/VehicleFiltersTests.cs` ("Apply filters" is `btn btn-outline-primary`), and a new `tests/MyGarage.Web.Tests/Vehicles/VehicleFormTests.cs` (required fields Nickname, Make, Model show a visible `*` marker with `aria-hidden="true"` and the form shows a "* Required" note; the form is constrained to `col-lg-8`; submit then Cancel button order)
- [X] T049 [P] [US2] Update `tests/MyGarage.Web.Tests/Vehicles/VehicleListTests.cs`, `tests/MyGarage.Web.Tests/Vehicles/VehicleEditPageTests.cs`, `tests/MyGarage.Web.Tests/Vehicles/VehicleCreatePageTests.cs`, and `tests/MyGarage.Web.Tests/ApiCheck/ApiCheckPageTests.cs` for the shared components: API failures render through the standard alert (message and "Sign in again" link unchanged); the empty garage renders the `Empty` empty state with action "Add your first vehicle" (`/vehicles/new`); no matches renders the `NoMatch` empty state with action "Clear search and filters" (`data-testid="no-match-clear"` preserved)

### Implementation

- [X] T050 [P] [US2] Create `src/MyGarage.Web/Common/EmptyState.razor` per `contracts/layout-components.md`: parameters `Message` (required), `ActionText`, `ActionHref`, `Variant` (`Empty` default, `NoMatch`), `Compact` (bool); makes T045 pass
- [X] T051 [P] [US2] Create `src/MyGarage.Web/Common/ApiFailureAlert.razor` per `contracts/layout-components.md`: parameters `Failure` (`GarageApiFailure`), `ReturnUrl` (app-relative path), `NotAcceptedMessage`, `UnavailableMessage`; renders the standard warning alert and, for `NotAccepted` only, a `btn btn-primary` "Sign in again" link to `/authentication/login?returnUrl=` plus `Uri.EscapeDataString(ReturnUrl)`; makes T046 pass
- [X] T052 [US2] Replace the copy-pasted failure alert blocks with `<ApiFailureAlert />` in `src/MyGarage.Web/Vehicles/VehicleList.razor` (return URL `/vehicles`), `VehicleDetail.razor` (`/vehicles/{VehicleId}`), `VehicleEdit.razor` (`/vehicles/{VehicleId}/edit`), `VehicleCreate.razor` (`/vehicles/new`), `VehicleJournalSection.razor` (section-level failure), and `src/MyGarage.Web/ApiCheck/ApiCheck.razor` (`/api-check`, using `ApiCheckMessages`); keep messages from `VehicleMessages` and `ApiCheckMessages` unchanged; use `<EmptyState>` with the not-found message for the 404 states in `VehicleDetail.razor` and `VehicleEdit.razor` (keep the "Back to my garage" link as the standard action, wording "Back to my vehicles")
- [X] T053 [US2] Adopt `EmptyState` in `src/MyGarage.Web/Vehicles/VehicleList.razor` (empty garage: `Variant=Empty`, message "You have no vehicles yet.", action "Add your first vehicle" to `/vehicles/new`; no matches: `Variant=NoMatch`, message "No vehicles match your search or filters.", action "Clear search and filters" to `/vehicles` keeping `data-testid="no-match-clear"`) and as `Compact` in `VehicleTagsSection.razor`, `VehicleJournalSection.razor`, and `VehicleGallerySection.razor` replacing their ad-hoc "No ... yet" paragraphs (existing message text unchanged)
- [X] T054 [P] [US2] Standardize forms: in `src/MyGarage.Web/Vehicles/VehicleForm.razor` add a visible required marker (`<span class="text-danger" aria-hidden="true"> *</span>` inside the `form-label`) for Nickname, Make, and Model, a `<p class="form-text">* Required</p>` note, constrain the form to `col-lg-8` (wrapper `div class="row"` > `div class="col-lg-8"`), keep submit then Cancel; in `VehicleTagsSection.razor` render the add-tag error as `invalid-feedback d-block` (not `text-danger small`) keeping `id="tag-error"`, `role="alert"`, and the input's `aria-describedby`; in `VehicleFilters.razor` change "Apply filters" from `btn-secondary` to `btn-outline-primary` and render the criteria errors with the standard `alert alert-danger` (unchanged markup) inside `role="alert"`
- [X] T055 [P] [US2] Standardize destructive confirmations (inline `<details>` pattern, danger alert, POST form): align wording and sizing to "Yes, delete this {vehicle|note|picture}" and "Keep {vehicle|note|picture}" in `src/MyGarage.Web/Vehicles/VehicleDetail.razor` (page-level, regular-size buttons), `src/MyGarage.Web/Vehicles/JournalNoteItem.razor` and `src/MyGarage.Web/Vehicles/VehicleGallerySection.razor` (row-level, `btn-sm`); keep `data-testid` values (`delete-vehicle`, `delete-note`, `delete-image`) and all existing `aria-label`s; leave tag removal as the documented one-click exception (accessible name "Remove tag {name}" kept)
- [X] T056 [P] [US2] Standardize cards and feedback: in `src/MyGarage.Web/Vehicles/VehicleCard.razor` confirm long nicknames, makes, and models wrap or truncate without breaking the grid (add `text-break` to the title link and detail lines; no inline styles); in `src/MyGarage.Web/Vehicles/VehicleGallerySection.razor` keep the `alert-{level}` notices but map levels to the standard set (`success` with `role="status"`, `warning`/`danger` with `role="alert"`); ensure each feedback `alert` in the Web project carries a role (makes T047 pass)
- [X] T057 [US2] Run `UiStandardConformanceTests` and fix any remaining one-off styles it reports (for example solid `btn-secondary`, inline `style=`, stray `<h1>`), then run all Web tests
- [X] T058 [US2] Create `docs/ui-standard.md` documenting: the logo (the three assets, usage on dark vs light surfaces, minimum size 16 px for the favicon and 24 px for the mark, clear space of at least a quarter of the mark's height, do not stretch/recolor/rotate); the brand palette with the final hex values and the contrast ratios recorded in T019; typography scale (page title `h2` in `PageHeader`, section `h2` styled `h5`, body, small/secondary); spacing (content gutters from `container-fluid`, `py-3` page padding, `mb-3` header, `gap-2`/`gap-3` between controls); the page frame (skip link, fixed bar, `main#main-content`); the trail and title table from `contracts/layout-components.md`; the standard patterns table (buttons, destructive confirmation including the tag-removal exception, forms, cards, feedback, headings, `EmptyState`, `ApiFailureAlert`); the accessibility rules (focus, icon-only names, reduced motion); and an attribution line for Bootstrap Icons (MIT) used by `Icon.razor`; link the page from `docs/README.md`

**Checkpoint**: One documented visual language; the conformance test guards against drift.

---

## Phase 7: User Story 3 - Enriched, welcoming home page (Priority: P3)

**Goal**: The home page greets the user, describes the product, and offers clear entry points (view the garage, add a vehicle), emphasizing "Add your first vehicle" when the garage is empty; signed-out visitors are taken to sign in.

**Independent Test**: Signed in with and without vehicles, open the home page and confirm the greeting, description, and entry points; signed out, open `/` and confirm the redirect to sign-in and return to `/` afterwards.

### Tests first (confirm they fail)

- [X] T059 [P] [US3] Update `tests/MyGarage.Web.Tests/Home/HomeTests.cs`: the page includes a short description paragraph about My Garage; a "My vehicles" link to `/vehicles` and an "Add vehicle" link to `/vehicles/new`; with zero vehicles (API fake returns `TotalCount` 0) the primary-styled action reads "Add your first vehicle" and "My vehicles" is secondary-styled; with at least one vehicle "My vehicles" is `btn btn-primary` and "Add vehicle" is `btn btn-outline-primary`; when the vehicles API call fails the page still renders both entry points (neutral styling) and no error alert
- [X] T060 [P] [US3] Add a test to `tests/MyGarage.Web.Tests/Authentication/LoginRedirectTests.cs`: an anonymous `GET /` is redirected to `/authentication/login?returnUrl=%2F` (the existing sign-in redirect behavior)

### Implementation

- [X] T061 [US3] Update `src/MyGarage.Web/Home/Home.razor`: change to static SSR so the page can call the API with the user's token (remove `@rendermode InteractiveServer`, add `@attribute [ExcludeFromInteractiveRouting]`, inject `VehiclesApiClient`); read the vehicle count with `Api.ListAsync(VehicleListCriteria.None, 1)` (`TotalCount`) inside a try/catch on `GarageApiException` that falls back to "unknown count"; render under the `PageHeader`: a short description paragraph ("My Garage keeps a private list of your vehicles, their details, tags, journal notes, and pictures."), and entry-point actions as in T059; keep `ClaimsPrincipalExtensions.GetDisplayName()` for the greeting; makes T059 and T060 pass

**Checkpoint**: All user stories are complete and independently verifiable.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T062 [P] Apply palette tokens to the remaining shared chrome in `src/MyGarage.Web/Components/Layout/MainLayout.razor.css` (`#blazor-error-ui`) and `src/MyGarage.Web/Components/Layout/ReconnectModal.razor.css` so they use the brand tokens rather than hard-coded colors, without changing behavior; keep text contrast >= 4.5:1
- [X] T063 [P] Remove dead CSS from `src/MyGarage.Web/wwwroot/app.css` that is no longer referenced (for example any old `.navbar`/header rules) and keep the existing validation, error-boundary, and `.note-body` rules
- [X] T064 Run the full quality gate: `dotnet build MyGarage.slnx` (zero warnings) and `dotnet test MyGarage.slnx` (all pass, including the Aspire `MyGarage.AppHost.Tests` smoke test)
- [X] T065 Execute every scenario in `specs/003-ui-branding-standards/quickstart.md` (identity, navigation bar, page header and layout table, standardization, home page), including keyboard-only navigation, 320 px width, 200% and 400% zoom, a contrast check, and the reduced-motion setting; fix any defect found and re-run T064
- [X] T066 Final review against `.specify/memory/constitution.md`: no new packages or projects, no API/DTO/database changes, Bootstrap 5.3.x only, `/faq` and `/about` are the only new anonymous pages and are justified in the spec (FR-028), no user data or secrets logged or committed

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks all user stories (`Crumb`, `PageHeader`, `Icon`, `GetInitials` are used by the navigation bar, FAQ/About, and every page).
- **US1 (Phase 3)**: depends on Foundational; delivers assets, palette, head tags, and `BrandLink`.
- **US1b (Phase 4)**: depends on US1 (`BrandLink`, palette tokens, `--mg-*` variables) and Foundational (`Icon`, `GetInitials`, `PageHeader` for FAQ/About). It replaces the temporary header from T018.
- **US1c (Phase 5)**: depends on Foundational (`PageHeader`); the full-width content area (acceptance scenario 8) is delivered by US1b's `MainLayout` rewrite, so verify that scenario after US1b.
- **US2 (Phase 6)**: depends on US1c (pages already use `PageHeader`; US2 edits the same page files afterwards) and US1b (`MainLayout`).
- **US3 (Phase 7)**: depends on US1c and on US2's `EmptyState`/components only for styling consistency; it can start after US1c.
- **Polish (Phase 8)**: depends on all desired stories.

### User story dependencies

- US1 -> US1b -> (US1c, US2, US3). US1c and US3 are otherwise independent; US2 touches many of the same page files as US1c, so run US2's page edits after US1c's.

### Within each phase

- Tests are written first and must FAIL before implementation.
- Same-file tasks are sequential: `MainLayout.razor` (T018, T026), `App.razor` (T017, T028), `app.css` (T016, T027, T063), `VehicleList.razor` (T037, T052, T053), `VehicleDetail.razor` (T038, T052, T055), `VehicleEdit.razor` (T039, T052), `VehicleCreate.razor` (T040, T052), `Home.razor` (T041, T061), `SharedHeadTests.cs` (T011, T023).

### Parallel opportunities

- Phase 2: T003, T004, T005 together; then T006, T007, T008 together (T009 after T006).
- Phase 3: T010, T011, T012 together; T013, T014, T015 together.
- Phase 4: T020 to T024 together; T029 and T030 together.
- Phase 5: T032 to T036 together; T037 to T043 together.
- Phase 6: T045 to T049 together; T050 and T051 together; T054, T055, T056 together.

```text
# Phase 5 example (after T031): write the tests in parallel, then adopt the header in parallel
T032 T033 T034 T035 T036   ->   T037 T038 T039 T040 T041 T042 T043   ->   T044
```

## Implementation Strategy

### MVP first (US1 + US1b + US1c)

The three P1 stories together form the branded frame (logo, fixed bar, page header) and are the minimum coherent release. Deliver them in order:

1. Phase 1 and Phase 2 (foundation).
2. Phase 3 (US1): logo, favicon, palette, `BrandLink`; stop and check the logo shows everywhere.
3. Phase 4 (US1b): the fixed bar, search, menus, FAQ/About; stop and check the signed-in and signed-out bars.
4. Phase 5 (US1c): the page header on every page; run T064 and the quickstart sections 1 to 3.

### Incremental delivery

5. Phase 6 (US2): shared components, standard patterns, conformance test, `docs/ui-standard.md`.
6. Phase 7 (US3): home page enrichment.
7. Phase 8: polish, full gate, quickstart.

Each phase ends in a green build (zero warnings) and passing tests before the next begins.
