# Research: UI Branding and Standardization

All decisions below resolve the open design questions for the spec. No `NEEDS CLARIFICATION` items remain.

## R1. How the navigation bar is built and made interactive

- **Decision**: Build `AppNavbar` as a plain static-SSR layout component using Bootstrap's `navbar`, `dropdown`, and `collapse` markup. Load Bootstrap's already-vendored `bootstrap.bundle.min.js` once from `App.razor` with `defer`. No Blazor event handlers in the bar.
- **Rationale**: `Routes` has no render mode, so `MainLayout` renders statically even when a page (the home page) is an interactive island; `@onclick` in the layout would silently do nothing. Bootstrap's JS uses delegated `data-bs-*` listeners that survive Blazor enhanced navigation, supports keyboard (Enter/Space/Escape/arrows), focus return, and `aria-expanded` handling out of the box. This satisfies "prefer Bootstrap components over custom JavaScript".
- **Alternatives considered**: Interactive Server layout with `@onclick` toggles (adds a circuit dependency to every page and breaks the static pages that call the API with `HttpContext`); hand-written dropdown JavaScript or a JS module (more code, more accessibility risk); `<details>` dropdowns (no outside-click or Escape handling, inconsistent styling).

## R2. Applying the brand palette to Bootstrap

- **Decision**: Theme by overriding Bootstrap 5.3 CSS custom properties in `wwwroot/app.css` (`--bs-primary`, `--bs-primary-rgb`, `--bs-link-color(-rgb)`, `--bs-link-hover-color(-rgb)`, button and focus-ring variables, plus brand-specific `--mg-*` tokens). No Sass build.
- **Rationale**: Bootstrap 5.3 documents CSS-variable theming; it adds no tooling or dependency and keeps Bootstrap pinned to a stock 5.3.x file. `.btn-primary` needs its component-level variables (`--bs-btn-bg`, `--bs-btn-border-color`, hover/active/disabled) overridden alongside `--bs-primary`.
- **Alternatives considered**: Compile a custom Bootstrap with Sass (new build step, forks the vendored file); override with ad-hoc selectors per page (the "one-off styles" the spec forbids).

## R3. Brand palette and contrast

- **Decision**: "Garage Blue" primary `#0B4F8A`, hover `#083A66`; "Night" navigation-bar background `#0F2A43`; "Amber" accent `#F5A623` (logo and highlights only, never body text on white); secondary slate `#5C6773`; success, warning, danger, and info keep the Bootstrap defaults. Body text and backgrounds keep Bootstrap defaults.
- **Rationale**: Contrast ratios (WCAG relative luminance): white on `#0B4F8A` about 8.4:1; white on `#0F2A43` about 14.7:1; `#F5A623` on `#0F2A43` about 7.2:1; white on `#5C6773` about 5.8:1; `#0B4F8A` link text on white about 8.4:1; hover `#083A66` on white about 11.6:1. All meet AA (4.5:1 text, 3:1 UI components). The Bootstrap default primary (`#0d6efd`) sits at about 4.5:1 with white and is not distinctive.
- **Alternatives considered**: Keep the default Bootstrap blue (no identity); a warm red/orange primary (weaker contrast for white text and clashes with the danger color).
- **Follow-up**: Final values are recorded in `docs/ui-standard.md`; implementation re-verifies ratios with a contrast checker.

## R4. Logo design and assets

- **Decision**: An original SVG mark (a rounded tile with a simple garage roofline and an amber headlight bar) shown next to the text "My Garage". Three hand-authored assets under `wwwroot/images/`: `logo-mark-on-dark.svg` (used in the dark bar), `logo-mark-on-light.svg` (documentation, future light surfaces), and `favicon.svg` (compact icon form with a simplified shape that reads at 16 px). The name is real text (not baked into the image).
- **Rationale**: SVG is sharp at all densities, tiny, themeable, and needs no image tooling. Keeping the name as text makes it selectable, translatable, accessible, and satisfies the fallback requirement (FR-005) automatically; the image gets `alt=""` because the adjacent text names the link. Static assets are already served anonymously and fingerprinted by `MapStaticAssets`.
- **Alternatives considered**: PNG/ICO sets (need raster tooling, blurry on high-density screens); logo with embedded text (not accessible, breaks if the font is missing); an icon font (extra dependency).
- **Notes**: Browsers still request `/favicon.ico` by default; the explicit `<link rel="icon" type="image/svg+xml">` is honored by all supported browsers, so no ICO is shipped now (an ICO fallback can be added later without layout change).

## R5. Icons

- **Decision**: An `Icon` component that renders a small set of inline SVGs (search, bell, question-circle, person-circle, plus a few used by empty states) with `aria-hidden="true"`; paths are copied from Bootstrap Icons (MIT) with attribution in `docs/ui-standard.md`. Controls that contain only an icon get an accessible name from visible-hidden text or `aria-label`.
- **Rationale**: The constitution forbids other CSS/UI frameworks and requires new packages be justified. Five or six inline SVGs avoid a new dependency, font loading, and layout shift, and inherit `currentColor` so they follow the palette.
- **Alternatives considered**: `bootstrap-icons` package or font (new dependency, larger payload, font flash); Unicode/emoji glyphs (inconsistent rendering, poor accessibility).

## R6. Fixed top bar, content offset, and narrow screens

- **Decision**: Use Bootstrap `fixed-top` with `container-fluid` (full width). Offset the page with a CSS variable `--mg-navbar-height` (about 3.5 rem) as `body` top padding and `scroll-padding-top`. On screens narrower than the `md` breakpoint the search bar is placed in a Bootstrap `collapse` opened by a search-icon toggle button; the notification, help, and profile controls stay in the bar. A visually-hidden-until-focused "Skip to main content" link is the first element and targets `<main id="main-content">`.
- **Rationale**: Delivers "100% width fixed" with no content hidden under the bar, and keeps every control reachable at 320 px without horizontal scrolling (FR-031). The expanded search panel is a user-opened, transient overlay.
- **Alternatives considered**: Sticky (non-fixed) bar (does not satisfy "fixed top"); hiding search on mobile (loses a feature); offcanvas (heavier, more JS).
- **Spec adjustment**: SC-008 reads "never hides page content at any scroll position or window width" which is satisfied while the transient search panel is closed; acceptance is checked in that state.

## R7. Navigation bar content for signed-in and signed-out visitors

- **Decision**: Signed in: brand (mark + name, links `/`), search form, notifications dropdown, help dropdown, profile dropdown. Signed out: brand, help dropdown, and a "Sign in" button linking `/authentication/login`. The profile toggle is an avatar circle of the user's initials (new `GetInitials()` on the claims principal, falling back to a person icon for the neutral name) with the display name beside it from `sm` upward; the menu header shows the full name, then "My vehicles" (`/vehicles`), a divider, and "Sign out" (antiforgery `POST /authentication/logout`, as today).
- **Rationale**: Mirrors the spec (FR-025 to FR-030) and keeps the existing sign-out contract. Initials avoid any dependency on profile photos (out of scope). The authentication state comes from the cascading authentication state already used by `LoginDisplay`.
- **Alternatives considered**: Showing search/notification controls to anonymous users (they would lead only to a login redirect); including Profile or Settings menu items (pages do not exist and are out of scope).

## R8. What the search bar searches

- **Decision**: The bar's search is a GET form (`role="search"`, `action="/vehicles"`, field `q`, `maxlength="100"`, matching the garage page's existing search box) that lands on the existing owner-scoped garage search. Submitting an empty value opens the full garage.
- **Rationale**: Reuses the validated, owner-scoped search with no API change (spec FR-026); bookmarkable; works with static SSR and without JavaScript.
- **Alternatives considered**: Global multi-entity search (out of scope, needs API); client-side live suggestions (needs interactivity and new endpoints).

## R9. Notification icon behavior

- **Decision**: A dropdown toggle with a bell icon and accessible name "Notifications" opening a menu panel with the text "You have no notifications." No badge or unread dot is rendered; the panel markup is the extension point for a later feature.
- **Rationale**: Satisfies "include now but not yet implement" with an interactive, accessible control instead of a dead or disabled button (a disabled button is not focusable and gives no explanation).
- **Alternatives considered**: A disabled button (inaccessible, unexplained); hiding the icon until implemented (contradicts the request).

## R10. Page header component and breadcrumb rules

- **Decision**: One `PageHeader` component used by every page, with parameters `Title`, `Trail` (list of `Crumb(Label, Href?)` after the implicit "Home"), and an optional `Actions` fragment. It renders a Bootstrap `breadcrumb` inside `<nav aria-label="Breadcrumb">` (last item `aria-current="page"`, never a link), a single `<h1 class="h2">` title, an actions slot aligned to the right of the title (wrapping below on narrow screens), and a `<PageTitle>` of `"{Title} – My Garage"`. Trail and title rules are in [contracts/layout-components.md](contracts/layout-components.md).
- **Rationale**: One component enforces the spec's rules (FR-033 to FR-036) and replaces five slightly different hand-written headings. `FocusOnNavigate Selector="h1"` in `Routes.razor` keeps working. Rendering `<PageTitle>` inside the component keeps the tab title and h1 in sync.
- **Alternatives considered**: Layout-level header driven by route metadata (static SSR pages with item names would need a service or cascading state to receive item names; more indirection); per-page markup (what exists today, inconsistent).
- **Detail**: The vehicle detail "Item" crumb and title use the vehicle nickname; error and not-found states use generic titles ("Vehicle not found") with a trail that omits the item.

## R11. Content area margins

- **Decision**: `<main id="main-content" class="container-fluid py-3">`. Bootstrap's default `container-fluid` gutter (0.75 rem each side) is the "normal narrow margin".
- **Rationale**: Literal reading of the request: full-width content with Bootstrap's standard small gutters, as opposed to the centered fixed-width `container` used today. Pages that need readable line length (FAQ, About, forms) constrain themselves with column classes, not the layout.
- **Alternatives considered**: Keep `.container` (centered column; contradicts the request); `.container-xxl` (still centered).

## R12. Standard components for repeated page patterns

- **Decision**: Add `EmptyState` (message, optional action link) and `ApiFailureAlert` (failure kind, return URL; renders the existing "not accepted / unavailable" messages and a "Sign in again" action) in `Common/`. The existing inline confirmation pattern for destructive actions (a `<details>` disclosure with a danger alert and POST form that works under static SSR) is kept and documented as the standard; vehicle, image, and note deletions are audited against it; tag removal stays one-click because it is immediately reversible (documented exception).
- **Rationale**: These blocks are copy-pasted across at least five pages with small differences, and the spec requires one pattern (FR-012, FR-015). Two tiny components remove the drift without introducing a design system.
- **Alternatives considered**: A modal confirmation (needs JS or interactivity; the `<details>` pattern already works and is tested); documenting the pattern without components (drift returns).

## R13. FAQ and About pages

- **Decision**: Two static pages in `Help/`, each `[AllowAnonymous]`, using `PageHeader` with trail `Home / FAQ` and `Home / About`. FAQ holds a short accordion (Bootstrap `accordion`, native HTML headings and buttons) with starter questions (adding vehicles, tags, journal notes, pictures, privacy of data); About describes the product, version-neutral. Content is plain text in Razor markup.
- **Rationale**: Needed so the help menu has real destinations (FR-028); anonymous access is justified because they contain only general text and the help icon is also shown to signed-out visitors.
- **Alternatives considered**: External documentation site (none exists); sign-in required (see Complexity Tracking in the plan).

## R14. Removal of the old header, nav links, and API check access

- **Decision**: Delete `NavMenu` and `LoginDisplay`; the new bar replaces them. The `/api-check` diagnostic page is unchanged but no longer linked from the header (spec FR-037); it remains reachable by address.
- **Rationale**: Honors the requested bar contents exactly; routes to main areas are the home page entry points, breadcrumbs, search, and the profile menu shortcut.
- **Alternatives considered**: Keep a nav-links row (not in the requested bar contents); link API check from the About page (a developer diagnostic does not belong in user-facing help).

## R15. Test strategy for presentation work

- **Decision**: Test-first bUnit tests for component behavior (trail rendering and `aria-current`, title formatting, signed-in vs signed-out bar parts and their order, search form action/method/field, sign-out form antiforgery and method, initials, `EmptyState`, `ApiFailureAlert`); `WebAppFactory` tests that `/faq` and `/about` return 200 anonymously and that rendered pages include the favicon link, theme color, skip link, and the Bootstrap bundle script; update existing page tests to the new header; manual checks (keyboard, 320 px width, zoom 200%, contrast, reduced motion) listed in the quickstart.
- **Rationale**: Constitution VIII requires tests for components with logic; visual properties (contrast, overlap, responsiveness) cannot be asserted reliably in bUnit and are verified manually against the quickstart checklist.
- **Alternatives considered**: Browser-automation visual regression (new tooling and Docker/browser dependency for little gain at this scale).
