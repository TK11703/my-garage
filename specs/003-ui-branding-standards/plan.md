# Implementation Plan: UI Branding and Standardization

**Branch**: `003-ui-branding-standards` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/003-ui-branding-standards/spec.md`

## Summary

Give My Garage a brand identity and one standard page frame. A new logo (SVG mark plus the "My Garage" name) appears in a full-width, fixed top navigation bar that also holds a vehicle search box, a placeholder notifications menu, a help (?) menu (FAQ, About), and a profile menu (name, shortcut to vehicles, Sign out). Signed-out visitors get a reduced bar (logo, name, help, Sign in). Below the bar, a full-width content area with Bootstrap's normal gutters hosts every page, and every page starts with a standard page header: a breadcrumb trail (`Home / Vehicles / <item>`) plus the page title and an optional primary action. A brand palette is applied by overriding Bootstrap 5.3 CSS custom properties, existing pages are brought in line (header, buttons, empty states, failure alerts, delete confirmation), and a written UI standard is added to `docs/`.

Technical approach: presentation only, entirely inside `MyGarage.Web`. Replace `NavMenu` and `LoginDisplay` with an `AppNavbar` layout component (static SSR; dropdowns use the Bootstrap JS bundle that is already vendored), add `PageHeader`, `EmptyState`, `ApiFailureAlert`, and an inline-SVG `Icon` component, add `Help/` pages (`/faq`, `/about`), add logo/favicon SVG assets, theme Bootstrap through `app.css` custom properties, and adopt `PageHeader` on every existing page. No API, database, DTO, package, or project changes.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (pinned by `global.json`); Razor components, CSS, SVG.

**Primary Dependencies**: Existing only: Blazor Web App, Bootstrap 5.3.x (CSS and the already-vendored `bootstrap.bundle.min.js`), `Microsoft.Identity.Web`. New packages: none. Icons are hand-inlined SVG (paths taken from Bootstrap Icons, MIT) so no icon font or package is added ([research.md](research.md) R5).

**Storage**: N/A. No data is stored or changed.

**Testing**: xUnit + bUnit for components (`PageHeader`, `AppNavbar`, `Icon`, `EmptyState`, `ApiFailureAlert`, initials helper); `WebAppFactory` integration tests for `/faq`, `/about` anonymous access and for the shared `<head>` (favicon link, Bootstrap bundle script) and for existing pages' headers; existing Web tests updated for the new header markup. Manual accessibility and responsive checks per [quickstart.md](quickstart.md).

**Target Platform**: Modern evergreen browsers (SVG favicon, CSS custom properties) from 320 px to 1920 px widths; keyboard and screen-reader operable.

**Project Type**: Web application (Blazor Web App UI); change confined to the UI project.

**Performance Goals**: No perceptible change in page load; logo and favicon are small fingerprinted static assets (each under 5 KB); no new blocking scripts (`bootstrap.bundle.min.js` is loaded `defer`).

**Constraints**: Bootstrap 5.3.x only (no other CSS framework, no jQuery); no Sass build step; module-scoped, narrow JavaScript (none is written; Bootstrap's own bundle only); layout and header components must work under static SSR (no `@onclick`); WCAG 2.2 AA contrast; honors `prefers-reduced-motion`; no user data in the header beyond the signed-in user's own display name; sign-out stays an antiforgery-protected POST.

**Scale/Scope**: 1 project touched (`MyGarage.Web`) plus its test project; ~6 new components, 2 new pages, 3 SVG assets, 1 CSS theme section, 1 doc page; 14 existing pages/sections restyled or re-headed.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. .NET 10 / C# only; nullable on; warnings as errors; async + `CancellationToken` | PASS | No new projects. New C# is small (view-model records, initials helper); no I/O added. |
| II. Feature-folder organization | PASS | Cross-feature layout components live in `Components/Layout/` and `Common/` (explicit cross-feature folders); the new FAQ and About pages live together in a new `Help/` feature folder; test folders mirror them. |
| III. Solution structure & dependency direction | PASS | Only `MyGarage.Web` and `MyGarage.Web.Tests` change. Web references only Shared and ServiceDefaults; no data access added. |
| IV. DTO boundary | PASS | No API or DTO changes. |
| V. Validated minimal APIs | N/A | No endpoints added or changed. The search box is a GET form to the existing `/vehicles` page, which already validates `q`. |
| VI. Dapper, PostgreSQL, SQL scripts | N/A | No persistence. |
| VII. Versioned migrations | N/A | No schema change. |
| VIII. Test-first, real dependencies | PASS | Component tests (bUnit) and page tests written first and observed failing; UI components with logic (`PageHeader`, `AppNavbar`, initials) have tests. No database is mocked because none is involved. |
| UI: Blazor Interactive Server + Bootstrap 5.3.x only | PASS | Theming overrides Bootstrap variables in CSS; dropdown/collapse use Bootstrap's bundled JS; no jQuery, no other framework, no icon library. Header components are static-SSR compatible inside the existing layout. |
| Auth: every endpoint requires authorization unless the spec justifies it | PASS (justified) | `/faq` and `/about` are anonymous pages justified in spec FR-028 (general product text only, no user data). Static assets were already anonymous. Sign-out remains an authenticated-session POST with antiforgery. |
| Observability / logging | PASS | No new logging; user names are rendered, never logged. |
| Data protection | PASS | The navigation bar shows only the signed-in user's own claims-derived name; the search form only navigates to the owner-scoped vehicle list. Nothing in the header exposes another user's data. |
| Dependencies justified | PASS | No new packages. Bootstrap bundle script was already in `wwwroot/lib`; it is now referenced from `App.razor` ([research.md](research.md) R1). |

**Post-design re-check (after Phase 1)**: unchanged; no violations. Complexity Tracking lists the two deliberate choices a reviewer should confirm.

## Project Structure

### Documentation (this feature)

```text
specs/003-ui-branding-standards/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── layout-components.md
│   └── web-routes.md
├── checklists/
│   └── requirements.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
docs/
└── ui-standard.md                         # NEW: logo usage, palette, typography, spacing, component patterns, trail/title rules, icon source attribution

src/MyGarage.Web/
├── Components/
│   ├── App.razor                          # + favicon <link>, theme-color meta, bootstrap.bundle.min.js (defer)
│   └── Layout/
│       ├── MainLayout.razor               # skip link + <AppNavbar/> + <main id="main-content" class="container-fluid"> ; replaces old header
│       ├── MainLayout.razor.css           # navbar offset, skip-link styles (error-ui styles kept)
│       ├── AppNavbar.razor                # NEW: fixed-top bar; brand, search, notifications, help, profile (signed-in) / help + Sign in (signed-out)
│       ├── BrandLink.razor                # NEW: logo mark + "My Garage" text link to `/` (used by AppNavbar)
│       ├── PageHeader.razor               # NEW: breadcrumb trail + h1 title + optional actions + <PageTitle>
│       ├── Crumb.cs                       # NEW: record Crumb(string Label, string? Href = null)
│       ├── NavMenu.razor                  # REMOVED (spec FR-037)
│       ├── LoginDisplay.razor             # REMOVED (replaced by profile menu in AppNavbar)
│       └── ReconnectModal.razor(.css/.js) # unchanged except palette tokens
├── Components/Pages/
│   ├── Error.razor                        # re-headed with PageHeader
│   └── NotFound.razor                     # re-headed with PageHeader
├── Common/
│   ├── Icon.razor                         # NEW: inline SVG icons (search, bell, question-circle, person-circle, ...)
│   ├── EmptyState.razor                   # NEW: consistent empty / no-match block with optional action
│   ├── ApiFailureAlert.razor              # NEW: standard "not accepted / unavailable" alert + "Sign in again" action
│   └── ClaimsPrincipalExtensions.cs       # + GetInitials()
├── Help/                                  # NEW feature folder
│   ├── Faq.razor                          # @page "/faq" [AllowAnonymous]
│   └── About.razor                        # @page "/about" [AllowAnonymous]
├── Home/Home.razor                        # PageHeader; welcome + entry points
├── Authentication/                        # SignedOut, AuthenticationError re-headed
├── ApiCheck/ApiCheck.razor                # re-headed
├── Vehicles/                              # VehicleList/Create/Edit/Detail re-headed; sections audited for buttons, empty states, delete confirmation
└── wwwroot/
    ├── app.css                            # brand tokens (Bootstrap variable overrides), standard utilities
    └── images/
        ├── logo-mark-on-dark.svg          # NEW: mark for the dark navigation bar
        ├── logo-mark-on-light.svg         # NEW: mark for light backgrounds
        └── favicon.svg                    # NEW: compact icon form

tests/MyGarage.Web.Tests/
├── Components/Layout/                     # PageHeaderTests, AppNavbarTests (signed-in / signed-out / narrow markup), MainLayoutTests
├── Common/                                # IconTests, EmptyStateTests, ApiFailureAlertTests, ClaimsPrincipalExtensionsTests (initials)
├── Help/                                  # FaqPageTests, AboutPageTests (anonymous 200 via WebAppFactory)
├── Home/, Authentication/, ApiCheck/, Vehicles/   # existing tests updated for the new header markup
└── Components/SharedHeadTests             # SharedHeadTests: favicon link and bootstrap bundle script present in the document head/body
```

**Structure Decision**: Keep the existing multi-project layout; all work is in `MyGarage.Web` (constitution III). Layout-level components use the existing `Components/Layout/` folder and cross-feature widgets use `Common/` (as `Pager` already does); FAQ and About form a small `Help/` feature folder with matching test folder (constitution II).

## Complexity Tracking

| Deviation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Anonymous `/faq` and `/about` pages | The help menu is shown in the signed-out bar too, and the pages hold only general product text. Spec FR-028 justifies the exception required by the constitution's "every endpoint requires authorization unless the spec justifies it". | Requiring sign-in would send visitors from the help menu into a login redirect, defeating the help affordance, and protects no data. |
| Referencing the Bootstrap JS bundle from `App.razor` | Dropdown and collapse components need it; the file is already vendored under `wwwroot/lib`. It is Bootstrap's own JavaScript, not a new framework. | Writing custom dropdown JavaScript or using Blazor interactivity in the layout would add code (and a circuit dependency for a static layout) to reimplement behavior Bootstrap already provides and tests. |
