# Implementation Plan: Service Reminders

**Branch**: `013-service-reminders` | **Date**: 2026-11-06 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/013-service-reminders/spec.md`

## Summary

A signed-in user sets up recurring **reminders** on a vehicle: an oil change every 5,000 miles, an inspection every year, a registration every two years, or anything else by distance, time, or both. Each reminder belongs to one vehicle. Reminders live in a new **Reminders** tab on the vehicle details page, with add, edit, pause, resume, delete, and mark-done pages in the same static-SSR style as registrations. A new **Reminders** page lists every reminder across the garage, most urgent first, and a new **Dashboard** page shows the metrics My Garage already holds (vehicles, service records, fuel and charging entries, drafts awaiting review, reminders overdue and due soon) plus the reminders needing attention, ready for more metrics later.

Status (Upcoming, Due soon, Overdue, Paused, Mileage needed) is never stored. One pure calculator in `MyGarage.Api` derives it from the reminder, the vehicle's current mileage (the same greatest-of rule the vehicle list uses), and today's date, so the vehicle tab, the garage list, the dashboard, and the notifier all agree. Notifications reuse the existing `notifications` table and navigation bell: a migration adds two kinds (`reminder_due_soon`, `reminder_overdue`) and a `reminder_id` link. A `ReminderEvaluator` compares each reminder's computed state with the state it last notified (`notified_state` column) and inserts at most one notification per state change. It runs right after the writes that can change a status (reminder create, edit, complete, resume; vehicle update; service record create, update, delete; draft acceptance) and hourly from a `ReminderSweepService` for date-based changes, built like the existing `DraftExpiryService`.

No new package, project, container, or infrastructure. One migration (`0016_create_reminders.sql`), two new tables, one altered table.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (pinned by `global.json`)

**Primary Dependencies**: Existing only: ASP.NET Core minimal APIs, FluentValidation, Npgsql, Dapper, Serilog, OpenTelemetry, Blazor static SSR, Bootstrap 5.3.x. **New**: none.

**Storage**: PostgreSQL via forward-only migration `0016_create_reminders.sql`: `reminders`, `reminder_completions`, and an altered `notifications` (new kinds, `reminder_id`). No blob storage.

**Testing**: xUnit; Testcontainers PostgreSQL for `MyGarage.Database.Tests` and `MyGarage.Api.Tests` (no mocked database); bUnit and `WebApplicationFactory` with AngleSharp for `MyGarage.Web.Tests`; `UiStandardConformanceTests` covers the new `.razor` files. Pure unit tests for the status calculator. Docker required for database and API tests.

**Target Platform**: Local development with Aspire; Azure for the deployed app. Nothing in `infra/` changes.

**Project Type**: Web application (Blazor static SSR + minimal API + PostgreSQL), Aspire-orchestrated.

**Performance Goals**: Reminder list, garage list, and dashboard answer in under 1 s for 95% of calls at the expected size (tens of reminders per user). Hourly sweep finishes in seconds. Spec SC-001 (set up a reminder in under a minute) and SC-002 (mark done in under 30 s) are form-flow targets, not server limits.

**Constraints**: Owner derived only from validated claims; every statement scoped to the owner through the parent vehicle in SQL (`vehicles.owner_id`); another user's reminder answers 404. Parameterized SQL in `.sql` files only; async with `CancellationToken`. No reminder name or mileage in logs. Today's date is the server's UTC calendar date from `TimeProvider`. Due-soon thresholds are fixed constants (500 miles, 30 days).

**Scale/Scope**: Tens of users, a handful of reminders per vehicle. Roughly 12 endpoints in two feature groups, two repositories, one evaluator, one hosted sweep, one migration, 16 SQL scripts, six Web pages or components for reminders, one dashboard page, one nav entry, one vehicle tab.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. .NET 10 / C# only; nullable on; warnings as errors; async + `CancellationToken` | PASS | All new code async and forwards tokens. |
| II. Feature-folder organization | PASS | New `Reminders/` and `Dashboard/` folders in Shared, Database, Api, and Web (and the matching test folders). Notification changes stay in the existing `Notifications/` folders. |
| III. Solution structure & dependency direction | PASS | No new project or reference. Web reaches data only through `RemindersApiClient` and `DashboardApiClient`; Database does not reference Shared (status and kinds cross the boundary as text and are mapped in the Api). |
| IV. DTO boundary | PASS | `ReminderDto`, `ReminderDetailDto`, `ReminderRequest`, `CompleteReminderRequest`, `ReminderStatus`, `DashboardDto`, and friends live in `MyGarage.Shared`; records stay internal to Database and the Api mapper; explicit mappers are tested. |
| V. Validated minimal APIs | PASS | `MapGroup` per feature; each request DTO validated by FluentValidation through `ValidationFilter<T>` before any data access; `ProblemDetails` errors; OpenAPI metadata declared (see `contracts/`). |
| VI. Dapper, PostgreSQL, SQL scripts | PASS | `ReminderRepository` and `DashboardRepository` with scripts in `Scripts/Reminders/` and `Scripts/Dashboard/`; no SQL in C#; ownership proved in SQL. |
| VII. Versioned migrations | PASS | `0016_create_reminders.sql`, forward-only; `0001`-`0015` untouched. Migration-count tests are bumped. |
| VIII. Test-first, real dependencies | PASS | Failing tests first for migration, repository, calculator, validators, every endpoint (success, validation failure, not found, other-owner), evaluator, sweep, notifications, and Web pages. Real PostgreSQL; nothing mocked. |
| Auth: Entra OIDC + JWT; every endpoint authorized | PASS | All endpoints are user endpoints under the fallback policy and refuse app-only tokens. None is anonymous or trusted-service. |
| Observability | PASS | Logs carry ids, counts, and outcomes only. New metrics: reminders evaluated and notifications raised, tagged by state. |
| Data protection: ownership from claims; SQL scoped by owner | PASS | A reminder is owned through its vehicle; completions through the reminder; notifications keep their own `owner_id`. Nothing in a request names an owner. |
| Dependencies justified | PASS | No new packages. |

**Post-design re-check (after Phase 1)**: unchanged. No Complexity Tracking entries.

## Project Structure

### Documentation (this feature)

```text
specs/013-service-reminders/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── api.openapi.yaml   # Reminders, garage reminders, dashboard, and the changed notification schema
│   └── web-routes.md      # Pages, tab, nav entries, query values, messages
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── MyGarage.Shared/
│   ├── Reminders/        # ReminderDto, ReminderDetailDto, ReminderCompletionDto, ReminderRequest, CompleteReminderRequest,
│   │                     # ReminderStatus, ReminderTimeUnit, ReminderPresets
│   ├── Dashboard/        # DashboardDto, DashboardReminderDto
│   └── Notifications/    # NotificationDto + ReminderId, VehicleId; kind values documented
├── MyGarage.Database/
│   ├── Reminders/        # ReminderRecord, ReminderCompletionRecord, ReminderForEvaluationRecord, ReminderRepository
│   ├── Dashboard/        # DashboardCountsRecord, DashboardRepository
│   ├── Notifications/    # NotificationRepository + InsertForReminder, MarkReadForReminder; NotificationRecord + ReminderId, VehicleId
│   └── Scripts/
│       ├── Migrations/0016_create_reminders.sql
│       ├── Reminders/    # VehicleExists, Insert, ListForVehicle, ListForOwner, GetById, Update, Delete, SetPaused, InsertCompletion,
│       │                 # UpdateLastDone, ListCompletions, RaiseVehicleOdometer, ListForEvaluation, SetNotifiedState
│       ├── Dashboard/    # Counts
│       └── Notifications/# InsertForReminder, MarkReadForReminder, List (join to reminders for vehicle id)
├── MyGarage.Api/
│   ├── Reminders/        # RemindersEndpoints, GarageRemindersEndpoints, ReminderMapper, ReminderRequestValidators,
│   │                     # ReminderStatusCalculator, ReminderEvaluator, ReminderSweepService, ReminderOptions, ReminderMetrics
│   ├── Dashboard/        # DashboardEndpoints, DashboardService
│   ├── Vehicles/         # VehiclesEndpoints: evaluate the vehicle after an update
│   ├── ServiceRecords/   # ServiceRecordsEndpoints: evaluate the vehicle after create, update, delete
│   ├── Drafts/           # DraftService: evaluate the vehicle after an accepted service record
│   └── Notifications/    # NotificationMapper: ReminderId, VehicleId
└── MyGarage.Web/
    ├── Reminders/        # RemindersApiClient, ReminderTab.razor (list inside the vehicle tab), ReminderCreate/Edit/Detail pages,
    │                     # ReminderForm.razor, ReminderFormModel, ReminderCompleteForm.razor, ReminderFormat, GarageRemindersPage.razor
    ├── Dashboard/        # DashboardApiClient, DashboardPage.razor
    ├── Vehicles/         # VehicleTabs (+ reminders tab), VehicleDetail.razor (+ pane)
    ├── Notifications/    # NotificationsMenu: reminder icon and link
    └── Components/Layout/NavMenu.cs  # + Dashboard and Reminders entries

tests/
├── MyGarage.Database.Tests/   # migration 0016 (count bumps), ReminderRepository, ownership, cascade, notification kinds
├── MyGarage.Api.Tests/        # calculator, validators, endpoints, evaluator, sweep, hooks, dashboard, notification mapping
└── MyGarage.Web.Tests/        # tab, pages, forms, garage list, dashboard, bell, UI conformance
```

**Structure Decision**: Two new feature folders per project, `Reminders` and `Dashboard`, because the dashboard is its own growing surface and must not be buried inside reminders. Notifications are extended in place rather than forked: a reminder alert is just another kind in the same table, list, badge, and dismiss flow. The status calculator is the single source of truth and lives in the Api, so no SQL duplicates the rule.

## Complexity Tracking

No constitution violations to justify.
