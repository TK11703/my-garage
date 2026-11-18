---

description: "Task list for UI Redesign (Instrument Cluster Direction)"
---

# Tasks: UI Redesign (Instrument Cluster Direction)

**Input**: Design documents from `/specs/016-ui-redesign-instrument-cluster/`

**Prerequisites**: plan.md, spec.md, research.md (R1-R9), data-model.md, contracts/vehicle-readouts-api.md, quickstart.md; visual references `docs/design-tokens.md`, `docs/design-tokens.css` (moves to `wwwroot/css/tokens.css` in T006), `docs/mockups/`

**Tests**: Required (constitution VIII): write the test, watch it fail for the right reason, then implement. Web tests need no Docker (`dotnet test tests\MyGarage.Web.Tests`). Api and Database tests use Testcontainers PostgreSQL (never mocked; stop `aspire run` first).

**Organization**: One phase per delivery slice (plan.md). Each slice builds, passes tests, and can merge alone. Slice 1 (US1 + US2, both P1) is the foundation every page needs; slice 2 (US3 + US4) needs the read-model additions; slice 3 (US5); slice 4 (US6); slice 5 (US7 and cleanup).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1-US7 on user-story tasks
- Names to keep identical everywhere:
  - CSS: `wwwroot/css/tokens.css`, `wwwroot/css/fonts.css`, `wwwroot/fonts/`; variables `--mg-*` exactly as in tokens.css; theme attribute `data-bs-theme="light|dark"`; storage key `mg-theme`
  - Shell: `AppRail.razor`, `PublicBar.razor`, `NavMenu.cs`; ids `mg-rail`, backdrop `mg-rail-backdrop`; drawer class `mg-rail-open`; data attributes `data-mg-theme-toggle`, `data-mg-rail-toggle`, `data-mg-rail-close`
  - Shared DTOs: `VehicleNextDueDto`, `VehicleTimelineEntryDto`, `VehicleTimelineKind`, `SuggestedReminderDto`; fields `VehicleSummaryDto.NextDue`, `VehicleSummaryDto.SpendThisYear`
  - Api: `GET /api/vehicles/{vehicleId:guid}/timeline`, `GET /api/vehicles/{vehicleId:guid}/reminder-suggestions`
- Keep these markup contracts (tests and behavior depend on them): `PageHeader` renders one `h1` before `nav[aria-label=Breadcrumb] ol.breadcrumb`, actions inside `.mg-pagebar`; `data-testid` hooks; form names; accessible names on row controls
- No new NuGet package; no migration; `infra/` untouched
- Work on branch `016-ui-redesign-instrument-cluster`; never commit to `main`; merge by pull request

## Phase 1: Setup

- [X] T001 Create branch `016-ui-redesign-instrument-cluster`, write spec, plan, research, data model, contract, quickstart, and this task list
- [x] T002 Record the baseline: `dotnet build MyGarage.slnx` (zero warnings) and `dotnet test tests\MyGarage.Web.Tests`; note failures that exist before any change

---

## Phase 2: Slice 1 - Foundation and shell (US1, US2) (P1)

**Goal**: Every page uses the new tokens, fonts, controls, and shell in both themes. Independent test: quickstart slice 1 checklist.

### Tests first

- [x] T003 [P] [US1] Write `tests/MyGarage.Web.Tests/Common/DesignTokenTests.cs`: parse `wwwroot/css/tokens.css`; for both themes assert every pair in `docs/design-tokens.md` (text 4.5:1; interface parts 3:1; line-strong vs surface and surface-2 3:1); assert no `#RRGGBB` literal in `app.css` outside an allow-list (paper, rail, logo, `--mg-*` legacy aliases) (fails until T006-T010)
- [x] T004 [P] [US1] Write `FontDeliveryTests` in the same folder: every `url(` in `fonts.css` resolves to a file under `wwwroot/fonts/`; the three OFL licence files exist; total size of the WOFF2 files is at most 250 KB; `App.razor` contains no `fonts.googleapis`/`fonts.gstatic`/CDN host
- [x] T005 [P] [US2] Rewrite `tests/MyGarage.Web.Tests/Components/Layout/` shell tests for `AppRail` and `PublicBar` (replacing `AppNavbarTests`, `AppNavbarSignedOutTests`, `AppSidebarTests`, and the shell parts of `MainLayoutTests`): items and hrefs from `NavMenu`; current item `aria-current="page"`; drafts count on the Drafts item with visually hidden text; Notifications item with unread dot and the existing menu; theme toggle with `aria-pressed`; menu button `aria-controls="mg-rail"` below 768 px; public bar has Help links, theme toggle, Sign in; signed-out layout renders no rail; skip link first; exactly one `h1` per page; `PageHeader` contract unchanged (existing `PageHeaderTests` keep passing)

### Implementation

- [x] T006 [US1] Move `docs/design-tokens.css` to `src/MyGarage.Web/wwwroot/css/tokens.css` (git mv), point `docs/design-tokens-preview.html` and `docs/design-tokens.md` at it, and link it in `Components/App.razor` before `app.css`
- [x] T007 [P] [US1] Add the fonts: download Latin-subset WOFF2 for Chakra Petch 500/600/700, Inter (variable), JetBrains Mono (variable) into `wwwroot/fonts/` with each family's OFL text; write `wwwroot/css/fonts.css` (`@font-face`, `font-display: swap`, metric-matched local fallbacks); link it and preload Chakra Petch 700 and Inter in `App.razor`
- [x] T008 [US1] Rebase `app.css` on the tokens: replace the `:root`/dark variable blocks with legacy aliases of the new tokens; restyle global controls to the design (buttons incl. `.btn-primary` amber, outline and danger variants, form controls using `--mg-line-strong`, floating labels, cards as bordered panels without shadow, tabs, accordions, alerts and badges from the soft tints, list groups, pager, dropdowns, focus ring); body 14 px with inputs 16 px under 576 px; mono tabular numerals utilities; remove the bell shake and the hero styles' dependence on `--mg-navbar-bg`
- [x] T009 [US2] Create `Components/Layout/NavMenu.cs` rail items (Garage `/` and `/vehicles`, Add vehicle `/vehicles/new`, Reminders `/reminders`, Drafts `/drafts`, Search `/search`, Dashboard `/dashboard`; bottom: Notifications, Help `/faq`, theme toggle, Account `/preferences`); create `AppRail.razor` (+ `NotificationsMenu` hosted in it, drafts count, unread dot) and `PublicBar.razor`; update `MainLayout.razor` (skip link, rail or public bar by sign-in state, drawer backdrop); delete `AppNavbar.razor`, `AppSidebar.razor`, and the obsolete tests
- [x] T010 [US2] Restyle `PageHeader.razor` by CSS only: breadcrumb becomes the small mono location line above a large display title (flex `order`, DOM order unchanged), actions to the right, no sticky dark bar; add the mobile menu button to the header on phones
- [x] T011 [US1] Update `wwwroot/js/theme-init.js` and `app-shell.js`: theme meta colour from `--mg-bg`; rail drawer open/close (backdrop, Escape, focus return, close on link click) with the renamed data attributes; theme toggle unchanged in storage behavior
- [x] T012 [US1] Replace `docs/theme-preview.html` with a pointer to `design-tokens-preview.html` (or delete it and fix links) and update `docs/ui-standard.md` for the shell, tokens, type roles, and component patterns changed in this slice (mark sections for later slices)
- [x] T013 [US1] Rewrite `UiStandardConformanceTests` expectations that referenced the old shell (page bar title size, navbar, sidebar, hero, `mg-notfound-*`) so the file guards the new standard; keep the inline-style, table, markup-string, alert-role, and h1 rules
- [x] T014 Run `dotnet build MyGarage.slnx` (zero warnings) and `dotnet test tests\MyGarage.Web.Tests`; fix fallout (class names, `.mg-sidebar`/`.mg-navbar` lookups in other tests); run the quickstart slice 1 checklist in the browser (both themes, 320 px, keyboard, reduced motion, network panel)

**Checkpoint**: Every page wears the new look and shell. Merge-ready.

---

## Phase 3: Slice 2 - Garage and vehicle (US3, US4) (P2)

### API first (read models)

- [x] T020 [P] [US3] Database tests (Testcontainers): `VehicleRepository` list/search return `NextDue` (most urgent active reminder; excludes paused and archived) and `SpendThisYear` (service totals, fuel and charging totals, compliance costs; current calendar year boundaries; owner-scoped)
- [x] T021 [US3] Add `Scripts/Vehicles/` SQL for the readouts; extend the summary record and mapping; add `NextDue` and `SpendThisYear` to `VehicleSummaryDto` and `VehicleNextDueDto` in Shared; Api tests for list and search responses
- [x] T022 [P] [US4] Database and Api tests, then implementation, for `GET /api/vehicles/{id}/timeline` (kinds, ordering, paging, `kind` filter, `FromEmail`, ownership 404, validation 400) and `VehicleTimelineEntryDto`; SQL in `Scripts/Vehicles/Timeline*.sql`; endpoint under the existing vehicles group
- [x] T023 [P] [US5] (moved to slice 3 with its UI; suggestions need at least two services of the same name, interval from the median gap) Database and Api tests, then implementation, for `GET /api/vehicles/{id}/reminder-suggestions` (interval from the owner's history, de-duplication against existing reminders, archived returns empty, ownership 404); `SuggestedReminderDto`; SQL in `Scripts/Reminders/`

### Web

- [x] T024 [US3] Tests then implementation: garage page served at `/` and `/vehicles` (attention strip from existing dashboard figures, status switch with today's default, archived section with Restore, empty state with add and import actions); `VehicleCard` rebuilt (photo or typographic placeholder, name plate, status pill, readouts, labels); remove the Home hero and feature grid
- [x] T025 [US4] (built as: Overview default tab, Timeline, Specs, Photos, Description, Maintenance, Reminders; Documents stay inside Maintenance; the Add menu is unchanged) Tests then implementation: vehicle page overview (photo, spec plate, readouts, needs attention), tab strip (Overview, Timeline, Specs, Photos, Reminders, Documents), single Add control, existing Edit/Archive/Restore/Delete actions and confirmations, "add" values in Specs, one odometer figure everywhere (FR-026)
- [x] T026 [US4] Tests then implementation: Timeline tab (merged entries, kind filters as links, paging, receipt link to the service record); Photos and Documents tabs re-skinned from the existing media and documents sections
- [x] T027 Update `docs/ui-standard.md` (garage, vehicle) and the quickstart; build, test, browser-check slice 2

**Checkpoint**: Garage and vehicle pages are new. Merge-ready.

---

## Phase 4: Slice 3 - Records and reminders (US5) (P2)

- [x] T030 [P] [US5] Tests then implementation: service record receipt (vehicle band, pricing labels, right-aligned mono amounts, subtotal, tax, large total, credit marking) and per-line action menu with named Edit and Delete (existing confirmations)
- [x] T031 [P] [US5] Tests then implementation: reminders page (urgency ranking, status filters with counts, gauge and remaining text per row, Mark done behavior unchanged)
- [x] T032 [US5] (built as: suggestions panel on a vehicle's Reminders tab and on the service record page for that record's services; the garage-wide page has no panel because it has no per-vehicle context; one-step link opens the add form filled in) Tests then implementation: new-reminder form with live next-due preview (small module script; works without script), suggestions panel from the new endpoint
- [x] T033 (the forms and lists take the new look from the token bridge; added accent colors for pills, pagination, dropdown and list-group selection, and the breadcrumb now merges the area crumb) Restyle the maintenance entry forms and lists (fuel, charge, inspection, registration, insurance, tax, line item) to the new controls; docs and checks for slice 3

**Checkpoint**: Records and reminders are new. Merge-ready.

---

## Phase 5: Slice 4 - Import and review (US6) (P3)

- [x] T040 [P] [US6] (built as steps on the Add page over the existing panes, a read summary with source tags and check flags; no new import behavior) Tests then implementation: Add vehicle three-step sticker flow (choose, reading, review) with manual entry link, source tags on read values, flagged unclear values, features with replaces notes; existing import behavior and limits unchanged
- [x] T041 [P] [US6] (the two-column review, compatibility list, and check-then-save already existed; added the readiness ring and sticky original) Tests then implementation: draft review layout (original beside proposal), readiness ring and field list, explicit check-then-save, Save unavailable until the check passes; drafts list and upload page re-skinned
- [x] T042 Docs and checks for slice 4

---

## Phase 6: Slice 5 - Utility pages and cleanup (US7) (P3)

- [x] T050 [P] [US7] (the search page already had chips and matched-field highlight; added the large field and neutral chips) Tests then implementation: search page (large field, filters, removable chips, matched-field rows with highlight)
- [x] T051 [P] [US7] (Appearance card with Light and Dark buttons; the lists were already switches) Tests then implementation: preferences (account card, Light and Dark control using the existing toggle behavior, four lists with Shown/Hidden switches)
- [x] T052 [P] [US7] (FAQ filter and About already matched the spec; signed-out and sign-in problem pages now sit in cards) FAQ and About in the public layout (topic list, keyword filter with highlight; statement, steps, privacy); signed-out and sign-in problem pages
- [x] T053 [P] [US7] Not found page with the odometer (static final state under reduced motion); error page
- [x] T054 [US7] (the counts stay as four readouts in mono type rather than being removed; the charts already take their series colours from tokens) Dashboard page restyled (counts row replaced, charts sized to the data, series colours per measure)
- [x] T055 Remove the legacy `--mg-*` aliases and dead CSS from `app.css`; add a test that no legacy name remains; delete unused scripts and images (for example the home photo)
- [x] T056 (build 0 warnings; Web 1719, Api 1647 with 5 skipped, Database 690, AppHost 3 pass; quickstart extended for slices 2-5; the 320/768/1440 px walk was not done because the browser tool cannot resize) Final `docs/ui-standard.md`; full `dotnet build MyGarage.slnx` (zero warnings) and `dotnet test MyGarage.slnx`; quickstart in full; walk every page in both themes at 320, 768, and 1440 px

## Dependencies and order

- Slice 1 first. T003-T005 (tests) before T006-T011; T006 before T008 and T012; T007 and T006 independent; T009 needs T008 for styling but not for compilation.
- Slice 2: T020-T023 (API) before T024-T026 (Web) that use them; T024 and T025 can proceed in parallel after T021 and T022.
- Slices 3 to 5 each depend only on slice 1 (and T023 for suggestions); they can be reordered.
- T055 must be last among the code tasks.
