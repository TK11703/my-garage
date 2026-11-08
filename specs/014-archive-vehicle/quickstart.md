# Quickstart: Validate Archive Vehicle

A validation guide for the finished feature. Contracts: [api.openapi.yaml](contracts/api.openapi.yaml), [web-routes.md](contracts/web-routes.md). Data: [data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK (pinned by `global.json`) and Docker running (Testcontainers PostgreSQL).
- Stop any running `aspire run` before running tests (file locks).
- Branch `014-archive-vehicle`.

## 1. Build and automated tests

```powershell
dotnet build MyGarage.slnx
dotnet test MyGarage.slnx --logger "console;verbosity=minimal"
```

Expected: zero warnings, all tests pass. The new tests cover:

| Area | Project | What it proves |
|---|---|---|
| Migration 0018 applies to an empty database; migration-count tests bumped | Database.Tests | `archived_at` exists and is null for existing rows; the partial index exists. |
| `VehicleRepository` archive, restore, status list and count, filter options | Database.Tests | Owner scoping in SQL, `active` default, `archived`, `all`, already-in-state and other-owner results, reminder notifications marked read in the archive transaction. |
| Reminder, dashboard, and draft scripts | Database.Tests | Archived vehicles left out of owner reminder lists and evaluation, kept in the vehicle's own list; fuel and charging kept in the spend and entry count; recent activity and counts exclude them; draft picker excludes them. |
| `ReminderStatusCalculator`, `ReminderOrdering` | Api.Tests | `Archived` overrides `Paused` and every urgency; ordering places it after Paused. |
| Archive and restore endpoints | Api.Tests | Success, not found, other owner, already in state (409), app-only token refused. |
| `status` validator and list endpoint | Api.Tests | Valid values, invalid value is a 400, default is active, archived date in the payload. |
| Write guard | Api.Tests | A POST, PUT, or DELETE on each of the 11 guarded route groups and on the vehicle update for an archived vehicle returns 409 with the archived problem type and changes nothing; reads still work; an unknown or other owner's vehicle still answers 404. |
| Route-walking conformance test | Api.Tests | Every non-read route under `/api/vehicles/{vehicleId}` other than archive, restore, and delete carries the guard. |
| Dashboard figures | Api.Tests | Vehicle and service record counts, reminder counts, attention list, and recent activity exclude archived vehicles; fuel and charging entry count and spend include them with `vehicleArchived`; an all-archived garage; restore returns every figure. |
| Reminder evaluator, sweep | Api.Tests | No notice for an archived vehicle; restore re-evaluates once; `notified_state` preserved; a user-paused reminder stays paused after restore. |
| Drafts | Api.Tests | Archived vehicle not offered or suggested; draft for an archived vehicle reads as no vehicle; include into an archived vehicle refused. |
| Vehicle list, vehicle page, sections, reminder rows, dashboard legend, drafts, notice | Web.Tests | Status links and badges, default active, kept across paging and other filters, archive and restore confirm panels, read-only sections, archived notice on a direct write URL, legend "(archived)", `UiStandardConformanceTests`. |

## 2. Run the app

```powershell
aspire run
```

Open the Web URL shown by Aspire (https://localhost:7080 locally). Ask the user to sign in first if the browser is not signed in, then use the integrated browser for the walk below.

## 3. Walk the scenarios

Prepare a garage with two vehicles. Give vehicle A a service record, two fuel entries and one charging entry in the last few months, a journal note, an image, and three reminders: an overdue Registration reminder, a due-soon Tax reminder, and one you pause yourself. Note the dashboard figures and the chart.

1. **Archive (Story 1).** Open vehicle A, choose **Archive**, cancel (nothing changes), then confirm. The page shows the archived alert and badge with today's date, and only Restore and Delete in the page bar. Open every tab: all history is there with no add, edit, or delete controls.
2. **Figures (Story 2).** Open **Dashboard**: the vehicle count and service record count dropped by A's share; Needs attention and the overdue and due-soon counts no longer include A's reminders; Recent updates no longer shows A's note. The fuel and charging entry count and every month of the spend chart are unchanged, and A's layer is labeled "(archived)" in the legend and the figures table. Open **Reminders** in the sidebar: A's reminders are absent.
3. **Reminders disabled, not deleted.** Open A's Reminders tab: all three reminders are still there, each with the Archived badge and no pause, resume, mark-done, edit, or delete controls. The bell shows no unread notice for A's reminders (any that were waiting are now read).
4. **Filter (Story 3).** Open **My Vehicles**: only B is listed (Active is the default). Choose **Archived**: only A, with the Archived badge and its date. Choose **All**: both. Apply a search text and then change the status: the search text is kept. Page through with several vehicles and confirm the status survives. Archive B too and confirm the Active view shows the "no active vehicles" message with a link to Archived.
5. **Read-only (Story 5).** Open A's `/vehicles/{id}/reminders/new` address directly: the archived notice with Restore and Back links appears, and nothing is saved. In **Drafts**, review a draft: only active vehicles are offered; a draft that named A shows no vehicle selected.
6. **Restore (Story 4).** Open A and choose **Restore**, confirm. A returns to the Active list; the dashboard returns to the noted figures; its reminders show their earlier statuses, and the one you paused is still Paused. Raise the vehicle's mileage so a reminder worsens, and confirm one notice is raised.
7. **Delete (Story 6).** Archive A again, then **Delete** it: the confirmation is unchanged in meaning, A and everything attached disappear from All. Open B (active) and open its Delete panel: the text ends with "To keep the history instead, archive the vehicle."
8. **Stale tab.** Open A in two tabs, archive in one, then try to save an edit or add a record in the other: the archived notice appears and nothing is saved. Archive again in the second tab: a "This vehicle was already archived." message.
9. **Isolation.** As a second user, open the first user's vehicle address, `/archive`, and `/restore`: not found, and nothing about the vehicle is revealed.

## 4. Merge gate

- Solution builds with zero warnings.
- All tests pass, including the route-walking conformance test and `UiStandardConformanceTests`.
- Migrations `0001` to `0018` apply to an empty database.
- No vehicle name, VIN, or record data appears in logs (check the log tests).
