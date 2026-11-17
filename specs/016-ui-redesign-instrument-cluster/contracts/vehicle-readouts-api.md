# Contract: Vehicle readouts API (slice 2)

All endpoints require a user token (not an app token), are owner-scoped in SQL, return RFC 9457 `ProblemDetails` on error, and are documented in OpenAPI. DTOs live in `MyGarage.Shared`.

## `GET /api/vehicles` and `GET /api/vehicles/search` (existing, extended)

Response items (`VehicleSummaryDto`) gain optional `nextDue` and `spendThisYear` (see data-model.md). Existing fields, paging, filters, and the `status` filter are unchanged. Clients that do not know the new fields ignore them.

| Status | Meaning |
|---|---|
| 200 | List with the new optional fields |
| 400 | Invalid filter (unchanged) |
| 401 | Not signed in |

## `GET /api/vehicles/{vehicleId}/timeline`

Query: `page` (default 1), `pageSize` (default 20, max 50), `kind` (optional: `service`, `fuel-energy`, `inspection`, `registration`, `insurance`, `tax`, `journal`).

| Status | Body |
|---|---|
| 200 | `PagedResultDto<VehicleTimelineEntryDto>`, newest first |
| 400 | Invalid `page`, `pageSize`, or `kind` (`ProblemDetails` with validation errors) |
| 401 | Not signed in |
| 404 | Vehicle not found or not owned by the caller |

## `GET /api/vehicles/{vehicleId}/reminder-suggestions`

| Status | Body |
|---|---|
| 200 | `IReadOnlyList<SuggestedReminderDto>`, at most 5, most recent first; empty when none |
| 401 | Not signed in |
| 404 | Vehicle not found or not owned by the caller |

An archived vehicle returns 200 with an empty list (reminders cannot be created for it).

## Test obligations (constitution VIII)

For each endpoint: success, validation failure (where it has parameters), not found (other owner's vehicle), unauthorized. Repository tests run against Testcontainers PostgreSQL and cover ownership scoping, year boundaries for spend, archived exclusion for next due, and suggestion de-duplication.
