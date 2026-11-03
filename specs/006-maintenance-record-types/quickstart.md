# Quickstart: Charging and Fueling, Inspections, Registrations, Insurance, and Taxes

Validation guide for the feature. Contracts: [API](contracts/api.openapi.yaml), [web routes](contracts/web-routes.md). Data: [data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK (pinned by `global.json`) and Docker or Podman running (Testcontainers and the Aspire PostgreSQL resource).
- Branch `006-maintenance-record-types` checked out; the existing Entra setup from `infra/entra/README.md` in place.

## Automated checks

```powershell
dotnet build MyGarage.slnx                      # must finish with 0 warnings
dotnet test tests\MyGarage.Database.Tests       # migration 0011 on an empty database, the five repositories
dotnet test tests\MyGarage.Api.Tests            # validators, mappers, endpoints (success, validation, not found, 401, cross-user, cross-vehicle)
dotnet test tests\MyGarage.Web.Tests            # accordion, lists, forms, edit and delete, UI conformance (no Docker needed)
```

Expected: all green, including that migrations apply cleanly to an empty database and that a second run is a no-op.

## Manual validation (Aspire)

```powershell
aspire run
```

Sign in, open a vehicle, and choose its **Maintenance** tab. Maps to the spec's user stories (US) and success criteria (SC).

1. **Layout (US7)**: three accordion items in order: Service Records, Charging and Fueling, Inspections, Registrations, Insurance, and Taxes. Service Records behaves exactly as before.
2. **Empty states (US1-US4, US8)**: each new list shows an empty state with its add action(s); the headings show no counts or dates.
3. **Fuel entry (US1, SC-001)**: choose "Add fuel": date today, price per gallon 3.459, total gallons 12.345, total 42.70. Expect a return to the Maintenance tab with Charging and Fueling open, showing "12.345 gal at $3.459/gal" and `$42.70`; the heading shows "(1)" and today's date.
4. **Calculated total (US1)**: add another fuel entry with the total empty: 3.459 x 12.345 = 42.70 shown as the total. Add one with a total that differs (for example 43.00): the entered total is kept.
5. **Charging entry (US1)**: choose "Add charge": price per kWh 0.1342, kWh delivered 28.5, total session cost empty. Expect `$3.82` and a "Charge" badge. Add a free session (price 0, total 0): accepted.
6. **Invalid entries (US1, SC-006)**: try a future date, a negative price, quantity 0, five decimals in a price, and a blank date. Expect a message beside each field and nothing stored.
7. **Inspection (US2)**: add Safety, today, cost 25, next due one year out; then Emissions with no next due date (shows "Not set"). Try a next due date before the inspection date: rejected.
8. **Registration (US3)**: add Washington, cost 78.75, expiring next year; add an older one that already expired: accepted. The later expiration is first.
9. **Insurance (US8)**: add Start date today, End date one year out, Duration Annual, Cost 1,240.00; then Start date today, End date six months out, Duration Bi-Annual, Cost 640.00; then a Monthly policy with dates already in the past (accepted). Expect "{start} to {end}", the cost with its frequency (for example "$1,240.00 Annual") on each row, the latest end date first. Leave Duration unchosen, or enter an end date on or before the start date: rejected beside the field.
10. **Tax (US4)**: add Personal Property, 312.40, Annual; add Purchase, 1,850.00, Once. Both listed, newest added first.
11. **Edit and delete (US5, US7)**: edit one entry of each kind (values prefilled; the fuel kind is read-only); delete one after cancelling once. Expect the counts and latest dates to update and the right accordion item to be open on return.
12. **Privacy (US6, SC-005)**: with a second user, request the first user's `.../edit` addresses and call the API list, get, update, and delete with the first user's vehicle and entry ids (and with the entry id through one of the second user's own vehicles). Expect 404 behavior and no data. Signed out, expect a sign-in redirect and a return to the page.
13. **Cascade (US6, SC-007)**: delete a vehicle that has all five kinds; confirm no rows remain in `fuel_energy_entries`, `inspections`, `registrations`, `insurance_policies`, or `taxes` for it. The delete confirmation text lists the new kinds.
14. **Paging (SC-004)**: with 25+ entries in a list, it pages 10 at a time, keeps the other lists unchanged, and stays usable at phone width.
15. **Existing data (SC-008)**: vehicles with none of the new entries, and their service records, display and edit exactly as before.

## Logs

Check the API and Web logs for the session: they contain ids, counts, and outcomes only, with no prices, amounts, dates of entries, states, or other entry values.
