# Data Model: UI Redesign (Instrument Cluster Direction)

No tables, columns, or migrations change. All additions are read models (DTOs) computed from existing data, owner-scoped in SQL.

## VehicleSummaryDto (extended)

Existing fields unchanged. New optional fields (null when not applicable):

| Field | Type | Meaning |
|---|---|---|
| `NextDue` | `VehicleNextDueDto?` | The most urgent active reminder for the vehicle; null when it has none, is archived, or all are paused. |
| `SpendThisYear` | `decimal?` | Sum of the vehicle's service-record totals (line items plus tax), fuel and charging totals, and inspection costs dated in the current calendar year (archived vehicles included); null when nothing was spent. Registration, insurance, and tax rows carry no payment date and are not counted. |

### VehicleNextDueDto

| Field | Type | Meaning |
|---|---|---|
| `ReminderId` | `Guid` | For linking. |
| `Name` | `string` | Reminder name. |
| `Status` | `ReminderStatus` | Overdue, DueSoon, Upcoming, MileageNeeded. |
| `MilesRemaining` | `int?` | Negative when overdue; null when no mile interval or mileage unknown. |
| `DaysRemaining` | `int?` | Negative when overdue; null when no time interval. |

Ordering of "most urgent" is the reminders page ordering (Overdue, DueSoon, MileageNeeded, Upcoming; ties by earliest due date, then fewest miles). Paused and archived reminders are excluded.

## VehicleTimelineEntryDto (new)

| Field | Type | Meaning |
|---|---|---|
| `Kind` | enum | Service, FuelEnergy, Inspection, Registration, Insurance, Tax, Journal. |
| `Id` | `Guid` | Id of the source record. |
| `OccurredOn` | `DateOnly` | Record date (service date, fill date, day the entry was added for registrations and taxes (insurance: its start date), since they carry no event date, note date). |
| `Title` | `string` | Headline (for example the service shop or the compliance type). |
| `Detail` | `string?` | Second line (odometer, shop, category). |
| `Amount` | `decimal?` | Money figure when the kind has one. |
| `OdometerReading` | `int?` | When recorded. |
| `FromEmail` | `bool` | Created from an email or upload draft. |

Paged, newest first, optional `kind` filter.

## SuggestedReminderDto (new)

| Field | Type | Meaning |
|---|---|---|
| `Name` | `string` | Recurring service description (for example "Synthetic oil change"). |
| `IntervalMiles` | `int?` | Proposed interval from the owner's own history (median gap), or a documented default. |
| `IntervalMonths` | `int?` | Proposed time interval. |
| `LastDoneOn` | `DateOnly` | Most recent matching service. |
| `LastDoneMileage` | `int?` | Odometer at that service. |
| `Basis` | `string` | Short sentence for the UI (for example "From your Oct 30 service"). |

Excludes services that already have a reminder with the same normalized name. Read-only: saving goes through the existing reminder form.

## Validation and authorization

- All three reads require an authenticated user and take the vehicle id from the route; ownership is verified in SQL (`owner_id` scoped). A vehicle the user does not own yields not found, as elsewhere.
- Paging parameters (`page`, `pageSize` 1 to 50) and `kind` are validated with FluentValidation; invalid values return 400 `ProblemDetails`.
- Archived vehicles return the same data (history is readable) with `NextDue` null.
