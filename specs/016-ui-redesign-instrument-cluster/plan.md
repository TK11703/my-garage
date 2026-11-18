# Implementation Plan: UI Redesign (Instrument Cluster Direction)

**Branch**: `016-ui-redesign-instrument-cluster` | **Date**: 2026-11-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/016-ui-redesign-instrument-cluster/spec.md`

## Summary

Replace the template-default presentation with the approved "instrument cluster" design, in light and dark themes, across every page. The work is mostly presentation (tokens, fonts, shell, component CSS, page layouts) delivered in five independently shippable slices, plus three small read-only API additions (next due and spend on the vehicle summary, a vehicle timeline, suggested reminders) that the garage, overview, and timeline need. No schema change, no new write path, no new NuGet package. Bootstrap 5.3.x remains the only UI framework; the design is expressed as CSS custom-property tokens overriding Bootstrap, plus component CSS and markup changes.

Approach per slice: keep existing markup contracts that tests and behavior rely on (page header with an `h1`, `nav[aria-label=Breadcrumb] ol.breadcrumb li`, action slots, `data-testid` hooks) and move the look with CSS wherever possible; replace markup only where the layout changes (shell, garage card, vehicle header, receipt, reminders rows).

## Technical Context

**Language/Version**: C# 14 on .NET 10; Razor components (static SSR, no `@rendermode`); plain CSS and small classic/module scripts.

**Primary Dependencies**: Existing only: Blazor Web App, Bootstrap 5.3.x (vendored in `wwwroot/lib`), bUnit, xUnit, AngleSharp. **No new packages.** New static assets: three font families as WOFF2 (SIL Open Font License, licence texts shipped alongside).

**Storage**: N/A for the UI. Read models in slice 2 reuse existing tables via new SQL scripts in `MyGarage.Database/Scripts/<Feature>/`; no migration.

**Testing**: xUnit. Web tests (bUnit, no Docker): `dotnet test tests\MyGarage.Web.Tests`. Api and Database tests use Testcontainers PostgreSQL. `UiStandardConformanceTests` is rewritten to guard the new standard. Contrast is checked by a unit test over the token file (see research R4).

**Target Platform**: Modern evergreen browsers; phone (320 px) to 1920 px; keyboard, screen reader, forced-colors, reduced-motion.

**Project Type**: Web application (Web, Api, Database, Shared, ServiceDefaults, AppHost) as in the constitution.

**Performance Goals**: Fonts add no more than 250 KB on first load, preloaded for the two critical faces, `font-display: swap`; no layout shift greater than 0.05 from font swap (metric-matched fallbacks).

**Constraints**: Zero build warnings; every page usable without script; no external requests for fonts or styles; DTO boundary and ownership scoping unchanged.

**Scale/Scope**: About 119 Razor files, one 1,741-line `app.css`, 11 page scripts, about 40 test files touched by shell and markup changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Note |
|---|---|---|
| I. .NET 10 and C# only | Pass | CSS and JS assets only, as today. |
| II. Feature-folder organization | Pass | Shell stays in `Components/Layout`, shared bits in `Common/`; new read-model code goes in the feature folders (`Vehicles/`, `Reminders/`, `Dashboard/`). |
| III. Dependency direction | Pass | Web reaches data only through the Api; new DTOs live in `MyGarage.Shared`; SQL in `MyGarage.Database`. |
| IV. DTO boundary | Pass | New read models are DTOs; mapping is explicit and tested. |
| V. Validated minimal APIs | Pass | New endpoints are `GET` under existing feature groups, authorized, owner-scoped, documented, `ProblemDetails` on error; query parameters validated. |
| VI. Dapper and SQL files | Pass | New queries are `.sql` files, parameterized, loaded and cached. |
| VII. Migrations | Pass | None needed. |
| VIII. Test-first, real dependencies | Pass | Tests are written first per slice; API and repository tests use Testcontainers. |
| UI: Blazor + Bootstrap 5.3.x only | Pass | Bootstrap stays; tokens override its variables. Fonts are assets, not a CSS framework. |
| Dependencies: justify new packages | Pass | None added. Fonts are static files (justified in research R1). |
| Branch per spec | Pass | `016-ui-redesign-instrument-cluster`. |

Re-check after Phase 1: unchanged (no violations).

## Project Structure

### Documentation (this feature)

```text
specs/016-ui-redesign-instrument-cluster/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── vehicle-readouts-api.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/MyGarage.Web/
├── wwwroot/
│   ├── css/
│   │   ├── tokens.css            # NEW: source of truth, moved from docs/design-tokens.css
│   │   └── fonts.css             # NEW: @font-face, metric-matched fallbacks
│   ├── fonts/                    # NEW: WOFF2 files + OFL licence texts
│   ├── app.css                   # slimmed over the slices; legacy --mg-* aliases removed in slice 5
│   └── js/                       # theme-init.js, app-shell.js updated; new small modules per slice
├── Components/
│   ├── App.razor                 # preload fonts, link tokens and fonts
│   └── Layout/
│       ├── AppRail.razor         # NEW (replaces AppNavbar + AppSidebar for signed-in pages)
│       ├── PublicBar.razor       # NEW (signed-out and public pages)
│       ├── MainLayout.razor      # picks rail or public bar; skip link; drawer
│       ├── PageHeader.razor      # same contract, new look
│       └── NavMenu.cs            # rail items
├── Home/ Vehicles/ Reminders/ ServiceRecords/ Drafts/ Dashboard/ Help/ Preferences/ ...   # per-slice page changes
src/MyGarage.Shared/              # slice 2: VehicleSummaryDto fields, VehicleTimelineEntryDto, SuggestedReminderDto
src/MyGarage.Api/                 # slice 2: Vehicles (summary readouts, timeline), Reminders (suggestions)
src/MyGarage.Database/Scripts/    # slice 2: Vehicles/, Reminders/ read queries
tests/
├── MyGarage.Web.Tests/           # shell, conformance, per-slice page tests
├── MyGarage.Api.Tests/           # new endpoints and mapping
└── MyGarage.Database.Tests/      # new repository queries
docs/ui-standard.md               # rewritten in slice 5 (progressively amended per slice)
```

**Structure Decision**: Existing multi-project layout; new shell components under `Components/Layout`, tokens and fonts as static assets under `wwwroot/css` and `wwwroot/fonts`.

## Delivery slices

Each slice builds, passes its tests, and can merge on its own. Pages not yet converted keep working because the foundation (slice 1) restyles shared controls globally.

| Slice | Stories | Outcome |
|---|---|---|
| 1. Foundation and shell | US1, US2 | Tokens, fonts, theme, rail, public bar, page header, global control restyle, standard and conformance tests. Every page looks new even before it is rearranged. |
| 2. Garage and vehicle | US3, US4 | Read-model additions (contract), garage cards and strip, vehicle overview, tabs, timeline, specs "add" values, Add menu. |
| 3. Records and reminders | US5 | Receipt layout, line menus, reminders ranking, gauge, form preview, suggestions. |
| 4. Import and review | US6 | Sticker steps, draft review layout, readiness ring. |
| 5. Utility pages and cleanup | US7 | Search, preferences (theme control), FAQ, About, signed-out, 404, dashboard; remove legacy aliases; final standard. |

## Complexity Tracking

No constitution violations. One notable choice: legacy `--mg-*` variable names are kept as aliases of the new tokens during slices 1 to 4 so unconverted pages keep working; they are removed in slice 5 (tracked as a task).
