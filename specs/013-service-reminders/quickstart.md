# Quickstart: Validate Service Reminders

A validation guide for the finished feature. Contracts: [api.openapi.yaml](contracts/api.openapi.yaml), [web-routes.md](contracts/web-routes.md). Data: [data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK (pinned by `global.json`) and Docker running (Testcontainers PostgreSQL).
- Stop any running `aspire run` before running tests (file locks).
- Branch `013-service-reminders`.

## 1. Build and automated tests

```powershell
dotnet build MyGarage.slnx
dotnet test MyGarage.slnx --logger "console;verbosity=minimal"
```

Expected: zero warnings, all tests pass. The new tests cover:

| Area | Project | What it proves |
|---|---|---|
| Migration 0016 applies to an empty database; migration-count tests bumped | Database.Tests | Schema, checks, cascade from vehicle and reminder to completions and notifications. |
| `ReminderRepository`, `DashboardRepository` | Database.Tests | Owner scoping in SQL, another owner's vehicle and reminder behave as absent, completion transaction, odometer raise never lowers. |
| `ReminderStatusCalculator` | Api.Tests | Table of distance-only, time-only, and combined reminders, thresholds at 500 miles and 30 days, month-end clamp, paused, no mileage. |
| Validators and endpoints | Api.Tests | Success, validation failure, not found, and other-owner paths for every operation; app-only tokens refused. |
| `ReminderEvaluator`, `ReminderSweepService` | Api.Tests | One notification per state change, none on repeat, no alert when the state eases, mark-done clears, hooks after vehicle update, service record writes, and draft acceptance. |
| Notifications with reminder kinds | Api.Tests, Web.Tests | `reminderId` and `vehicleId` in the list, bell link, dismiss. |
| Reminders tab, pages, forms, garage list, dashboard | Web.Tests | Rendering, presets, validation messages that keep input, empty states, counts, and `UiStandardConformanceTests`. |

## 2. Run the app

```powershell
aspire run
```

Open the Web URL shown by Aspire (https://localhost:7080 locally) and sign in. Ask the user to sign in first if the browser is not signed in.

## 3. Walk the scenarios

1. **Set up (Story 1).** Open a vehicle with a known mileage (say 40,000), choose **Add** at the top and then **Add reminder**, with the Oil change preset: every 5,000 miles, last done at 40,000. Expect next due 45,000 miles and Upcoming. Add Inspection every 1 year, last done 2026-03-15: next due 2027-03-15. Add a reminder for Equipment "Trailer hitch" and one for Location "Home garage permit": the names show on the rows.
2. **Combined interval.** Add "every 5,000 miles or 6 months": both due points show; the status follows the nearer.
3. **Validation.** Submit with no name, no interval, a future last-done date, and a negative mileage: each problem is named and the typed values stay.
4. **Status (Story 2).** Edit the vehicle's mileage to 44,700: Oil change becomes Due soon with "300 miles left". Set it to 45,200: Overdue, "200 miles overdue". Open **Reminders** in the sidebar: the garage list shows overdue first with the vehicle name.
5. **Mark done (Story 3).** On the Oil change reminder choose **Mark done** at 45,200 today. Expect Upcoming, next due 50,200, one history entry, and its notification read. Try a mileage below 40,000 and a future date: refused with messages. Tick "Also update the vehicle's mileage" with a mileage above the vehicle's: the vehicle's latest odometer reading rises; without the tick it does not.
6. **Notifications (Story 4).** Create a reminder already past due: the bell shows one new notification naming the reminder and vehicle and linking to it. Reload several times: no duplicates. Move a Due soon reminder to Overdue by raising the vehicle's mileage: exactly one more notification.
7. **Pause and delete (Story 5).** Pause a reminder: it shows Paused and leaves the overdue count; resume it: status returns. Delete one after confirming. Delete a vehicle with reminders: they and their notifications are gone.
8. **Dashboard (Story 6).** Open **Dashboard**: the four compact cards (vehicles, service records, fuel and charging entries, drafts awaiting review) match the garage; there are no separate overdue or due-soon cards. The Needs attention card lists up to five reminders, overdue first, each with a due-date tile and a status badge. Below it, side by side, the "Fuel and charging spend" card shows a fuel chart and a charging chart for the last 12 months, each month's bar stacked by vehicle, and the "Recent updates" card lists the last 10 journal entries across the vehicles with the vehicle's picture, note start, and age. Sign in as a second user (or an empty garage): all zeros and the add-vehicle invitation, and none of the first user's data.
9. **Isolation.** As the second user, open the first user's reminder URL: not found.

## 4. Sweep check (date-based alerts)

In tests, set the clock past a reminder's due-soon date and call `ReminderSweepService.RunOnceAsync`; expect one notification and an unchanged second run. Locally, the sweep runs hourly (`Reminders:Sweep:Enabled`, `Reminders:Sweep:IntervalMinutes`); set the interval to 1 to watch it.

## 5. Merge gate

- Solution builds with zero warnings.
- All tests pass, including `UiStandardConformanceTests`.
- Migrations `0001` to `0016` apply to an empty database.
- No reminder name, subject, or mileage appears in logs (check the log tests).
