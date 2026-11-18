# Research: UI Redesign (Instrument Cluster Direction)

## R1. Font delivery

**Decision**: Self-host WOFF2 files under `wwwroot/fonts/`, declared in `wwwroot/css/fonts.css`. Chakra Petch 500, 600, 700 (static, Latin subset); Inter and JetBrains Mono as variable fonts (Latin subset, weight axis). Preload Chakra Petch 700 and Inter. Ship each family's OFL licence text.

**Rationale**: Self-hosting avoids a third-party request, keeps the Content Security Policy tight, and works offline in development. Subsetting to Latin keeps the total under the 250 KB budget. The fonts are static assets, not packages, so no dependency review is needed beyond licences (all SIL OFL 1.1).

**Alternatives rejected**: Google Fonts CDN (third-party request, privacy, CSP); an npm font package (adds a build step the repo does not have); system fonts only (loses the design voice, which was the review's top finding).

**Fallbacks**: `Segoe UI`/`system-ui` for display and body, `ui-monospace`/`Cascadia Mono`/`Consolas` for mono, with `size-adjust` and `ascent-override` on local fallback faces to limit layout shift.

## R2. Where the tokens live

**Decision**: `src/MyGarage.Web/wwwroot/css/tokens.css` is the source of truth. `docs/design-tokens-preview.html` links it by relative path. `docs/design-tokens.css` is deleted. `app.css` keeps component CSS only.

**Rationale**: One file, no copy to drift. The preview page already computes contrast from live values.

**Legacy aliases**: during slices 1 to 4 `app.css` maps old names (for example `--mg-primary`, `--mg-page-bg`, `--mg-card-radius`) onto the new tokens so unconverted markup still renders; slice 5 removes them and a grep test asserts none remain.

## R3. Keeping markup contracts while changing the look

**Decision**: Keep the `PageHeader` markup (`h1`, `nav[aria-label=Breadcrumb] ol.breadcrumb`, action slot in `.mg-pagebar`) and restyle with CSS: breadcrumb becomes the small eyebrow line above a large title using flex `order`, the bar is no longer sticky or dark. The DOM order (heading before breadcrumb) stays, so about 30 breadcrumb tests and the `.mg-pagebar` action lookups keep passing.

**Rationale**: Large reduction in test churn with no behavior change.

**Alternatives rejected**: Replacing the header component outright (rewrites about 30 test files for no user benefit).

## R4. Contrast verification

**Decision**: A unit test parses `tokens.css`, resolves each theme's variables, and asserts the contrast pairs listed in `docs/design-tokens.md` (text 4.5:1, interface parts 3:1). The preview page shows the same numbers.

**Rationale**: Contrast regressions are otherwise invisible in code review. Hand calculation of the original mockup values already found two failures.

## R5. Notifications and search in the rail

**Decision**: The rail has a Notifications item (unread dot) that opens the existing `NotificationsMenu` dropdown, and a Search item linking to `/search`. The top-bar search box is removed. A compact theme toggle sits above Account.

**Rationale**: The mockups dropped both; the real app needs them. The existing menu component already works with static SSR dropdowns.

## R6. Read-model additions

**Decision**: Three read-only additions (see `contracts/vehicle-readouts-api.md`): next-due summary and spend this year on `VehicleSummaryDto`; `GET /api/vehicles/{id}/timeline`; `GET /api/vehicles/{id}/reminder-suggestions`. All owner-scoped in SQL.

**Rationale**: The garage cards, overview, and timeline show figures no current endpoint returns. Computing them client-side would require many calls per card.

**Alternatives rejected**: Client-side aggregation over existing list endpoints (N+1 calls per vehicle, pagination gaps); storing the figures (new write path and drift).

## R7. Mobile rail

**Decision**: Under 768 px the rail is hidden and a menu button in the page header opens it as a drawer (existing `app-shell.js` pattern: backdrop, Escape, focus return). From 768 px it is always visible at 68 px wide.

**Rationale**: Reuses proven behavior and its tests; the rail is narrow enough to need no tablet middle state.

## R8. Motion

**Decision**: Only CSS animations (odometer roll on 404, scan line on sticker reading, bar and ring fills, card hover lift) and all under a single `prefers-reduced-motion` rule in `tokens.css` that zeroes durations and disables animation. The notification bell shake is removed.

## R9. Photos and placeholders

**Decision**: A vehicle with no main picture shows a typographic placeholder (year, model, last six of VIN when known) on the photo gradient tokens; no generic car glyph. Real pictures are cropped with `object-fit: cover` at a fixed aspect ratio with a bottom gradient so the name plate stays legible.

**Rationale**: Owner photos vary widely (the review saw dealer watermarks); a fixed ratio and gradient keeps cards uniform.
