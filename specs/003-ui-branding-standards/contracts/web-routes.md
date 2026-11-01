# Contract: Web Routes (UI Branding and Standardization)

No API changes. This feature adds two pages and one new document-level contract (shared head and scripts); all existing routes keep their behavior and only change their header markup (see [layout-components.md](layout-components.md)).

## New pages

| Route | Purpose | Auth | Notes |
|---|---|---|---|
| `/faq` | Frequently asked questions about using My Garage | Anonymous (`[AllowAnonymous]`) | Bootstrap accordion with short starter answers. Contains only general product text. Linked from the Help menu. |
| `/about` | About My Garage | Anonymous (`[AllowAnonymous]`) | Short product description. Linked from the Help menu. |

Anonymous access is justified in spec FR-028: no user data, and the Help menu is also shown to signed-out visitors. Both pages use the standard frame, so the navigation bar shows the signed-out variant (logo, name, help, Sign in) to anonymous visitors and the full signed-in bar to signed-in users.

## Changed navigation (no new routes)

| Element | Target | Method | Notes |
|---|---|---|---|
| Brand (logo + name) | `/` | GET | On every page. |
| Search form | `/vehicles?q={text}` | GET | Signed-in only; `q` max 100 characters; handled by the existing garage page and its validation. |
| Help menu | `/faq`, `/about` | GET | Signed in and signed out. |
| Profile menu: My vehicles | `/vehicles` | GET | Signed in. |
| Profile menu: Sign out | `/authentication/logout` | POST (antiforgery) | Existing endpoint; unchanged contract. |
| Sign in button | `/authentication/login` | GET | Signed out. |

Removed: the header links Home, My garage, and API check (spec FR-037). `/api-check` still works at its address.

## Document-level contract (all pages, `App.razor`)

| Element | Value |
|---|---|
| Favicon | `<link rel="icon" type="image/svg+xml" href="images/favicon.svg">` (fingerprinted via asset mapping) |
| Theme color | `<meta name="theme-color" content="#0F2A43">` |
| Title | Provided by `PageHeader` as `"{Title} – My Garage"`; pages without `PageHeader` are not allowed |
| Scripts | `lib/bootstrap/dist/js/bootstrap.bundle.min.js` with `defer`, before `_framework/blazor.web.js` |
| Static assets | `images/*.svg`, `app.css`: served anonymously (existing `MapStaticAssets().AllowAnonymous()`) so the logo and favicon load on sign-in, signed-out, and error pages |

## Status-code behavior

Unchanged. Unknown routes and re-executed status pages (`/not-found`) and the error page render inside the standard frame with `PageHeader`. Not-found vehicle pages keep returning HTTP 404 while showing the standard header.
