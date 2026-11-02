# Quickstart: Vehicle Service Records

Validation guide for the feature. Contracts: [API](contracts/api.openapi.yaml), [web routes](contracts/web-routes.md). Data: [data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK (pinned by `global.json`) and Docker or Podman running (Testcontainers and the Aspire PostgreSQL resource).
- Branch `005-service-records` checked out; the existing Entra setup from `infra/entra/README.md` in place.

## Automated checks

```powershell
dotnet build MyGarage.slnx                      # must finish with 0 warnings
dotnet test tests\MyGarage.Database.Tests       # migrations 0009/0010 on an empty database, repositories, cost calculator
dotnet test tests\MyGarage.Api.Tests            # validators, mappers, endpoints (success, validation, not found, 401, cross-user, 409)
dotnet test tests\MyGarage.Web.Tests            # tab, list, record page, forms, delete confirmations, UI conformance (no Docker needed)
```

Expected: all green, including that migrations apply cleanly to an empty database and that a second run is a no-op.

## Manual validation (Aspire)

```powershell
aspire run
```

Sign in, then walk through each scenario. Maps to the spec's user stories (US) and success criteria (SC).

1. **Empty state (US1)**: open a vehicle, choose the **Service Records** tab. Expect the empty state and an "Add service record" action; no summary line.
2. **Add a record (US1, SC-001)**: add Date serviced = today, Odometer = 45,000, Shop = "Joe's Garage". Expect a redirect to the record page with total cost `$0.00` and the empty line item state.
3. **Flat cost line item (US3)**: add "Tire rotation" with Flat cost 40. Expect total `$40.00`.
4. **Breakdown line item (US3)**: add "Oil change" using Labor and parts: 1.5 hours at $90.00, parts "Oil filter" 1 x $12.50 and "Oil" 5 x $8.25. Expect the line total `$188.75` (135.00 + 12.50 + 41.25) and record total `$228.75`.
5. **Pricing rules (US3, SC-005)**: try a line with both flat cost and labor (the unselected group is discarded), labor hours with no rate, a part with no price, and quantity 0. Expect messages beside the fields and nothing stored.
6. **Ordering and summary (US1, US6)**: add a second record dated earlier. Expect the later date first, and the tab summary "2 service records" and "Last serviced {later date}".
7. **Edit and delete (US2, US3)**: edit a record's shop; edit a line item (switch flat to breakdown); delete a line item and the whole record after cancelling once. Expect totals and the summary to update.
8. **Garage mileage (US5)**: on My Vehicles, the card shows the greater of the latest-dated record's odometer, the vehicle's Latest Odometer Reading, and the existing purchase mileage. Set Latest Odometer Reading to 60,000 on the vehicle: the card shows 60,000. Add a record at 70,000: the card shows 70,000. Delete it: the card returns to 60,000. The minimum/maximum mileage filters use the same value.
9. **Privacy (US4, SC-004)**: with a second user, request the first user's `/vehicles/{id}/service-records/{recordId}` and `.../edit` addresses and call the API with that record or line item id (and through one of the second user's own vehicles). Expect 404 behavior and no data. Signed out, expect a sign-in redirect and return.
10. **Cascade (US4, SC-006)**: delete a vehicle that has records; confirm no rows remain in `service_records`, `service_line_items`, or `service_line_item_parts` for it.
11. **Paging (SC-003)**: with the sample or a script creating 25+ records, the tab pages 10 at a time and stays usable on a phone-width window.
12. **Existing data (SC-007)**: vehicles without records display and edit exactly as before; their card mileage is unchanged.

## Logs

Check the API and Web logs for the session: they contain ids, counts, and outcomes only, with no shop names, descriptions, part names, or amounts.
