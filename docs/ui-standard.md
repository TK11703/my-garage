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

The search box submits `GET /vehicles?q=` (the existing owner-scoped vehicle search). Notifications is a placeholder menu with no unread indicator. Help links to `/faq` and `/about`, which are viewable without signing in. The account menu shows the user's own name, a shortcut to their vehicles, and Sign out (antiforgery `POST /authentication/logout`).

## Page header: trail and title

`<PageHeader Title="..." Trail="@(new[] { new Crumb("Vehicles") })">` renders "Home" plus the trail, the title, and an optional primary action at the right. The last crumb is the current page (no link). The title also sets the browser tab title to "{Title} – My Garage".

| Page | Route | Trail (after Home) | Title |
|---|---|---|---|
| Home | `/` | none | Welcome, {name} |
| Vehicle list | `/vehicles` | Vehicles | My Vehicles |
| Add vehicle | `/vehicles/new` | Vehicles > New vehicle | Add Vehicle |
| Vehicle detail | `/vehicles/{id}` | Vehicles > {nickname} | {nickname} |
| Edit vehicle | `/vehicles/{id}/edit` | Vehicles > {nickname} > Edit | Edit {nickname} |
| Vehicle not found | detail or edit | Vehicles | Vehicle not found |
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
| Destructive confirmation | Inline `<details>` with a danger alert, a POST form, and buttons "Yes, delete this {vehicle, note, picture}" and "Keep {vehicle, note, picture}". Tag removal is immediately reversible, so it is a one-click remove button with the accessible name "Remove tag {name}" |
| Forms | `form-label` above each control; required fields marked with a visible `*` (`aria-hidden`) and a "* Required" note; field errors use `invalid-feedback d-block`; forms limited to `col-lg-8` |
| Cards | Bootstrap card with image or placeholder, title link, secondary details; long text uses `text-break` |
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
