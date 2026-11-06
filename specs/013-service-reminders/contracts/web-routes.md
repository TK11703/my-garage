# Web Routes and UI Contract: Service Reminders and Dashboard

All pages are statically rendered (no interactive circuit) and require sign-in. Follow `docs/ui-standard.md`; `UiStandardConformanceTests` scans the new `.razor` files. No JavaScript is added.

## Navigation

| Where | Change |
|---|---|
| Sidebar (`NavMenu.cs`) | **Dashboard** (`/dashboard`) directly under Home; **Reminders** (`/reminders`) in the garage group beside My Vehicles. The home page is unchanged. |
| Vehicle page tabs (`VehicleTabs.All`) | New tab `reminders`, label **Reminders**, after Maintenance. The edit form omits it, like Maintenance. `?tab=reminders` opens it; unknown values still mean Details. |
| Vehicle delete confirmation | The text also lists reminders among what is removed. |
| Bell (`NotificationsMenu`) | A reminder notification shows the bell-style icon, links to `/vehicles/{vehicleId}/reminders/{reminderId}`, and keeps the existing dismiss form. |

## Vehicle page, Reminders tab (`/vehicles/{id}?tab=reminders`)

A list of the vehicle's reminders, most urgent first. Each row shows the name, the applies-to label with the equipment or location name when set, a status badge (Overdue danger, Due soon warning, Upcoming secondary, Mileage needed info, Paused light), the next due mileage and date with the remaining amount ("1,200 miles left", "due in 18 days", "400 miles overdue"), and a link to the reminder. A reminder with both intervals shows both lines and emphasizes the part at or past its threshold. Header action **Add reminder** links to `/vehicles/{id}/reminders/new`. An empty list shows the standard empty state explaining reminders with the same action. Status is never conveyed by color alone: the badge always has text.

## `GET /vehicles/{id}/reminders/new` and `POST` (form `create-reminder`)

| Query | Meaning |
|---|---|
| `preset` (optional) | `oil-change`, `tire-rotation`, `inspection`, `registration`, or `custom`. Fills the form from `ReminderPresets`; any other value means custom. |

Form fields: Type (preset links above the form, with Custom), Name, Applies to (radio group: Vehicle, Equipment, Location), Equipment or location name (shown for Equipment and Location; the field is always in the markup and is validated server-side, since no script runs), Repeat every miles, Repeat every (number) and unit (Months or Years), Last done on (defaults to today), Last done mileage (defaults to the vehicle's current mileage). A non-blocking warning appears above the form when the vehicle already has a reminder with the same name. If the vehicle has no recorded mileage, a note says a distance-based reminder cannot show a status until it has one. Validation messages name each field and keep the typed values (FR-020). On success: redirect to `/vehicles/{id}?tab=reminders&saved=created`.

## `GET /vehicles/{id}/reminders/{reminderId}`

The reminder detail page: header with the name and status badge; Details card (applies to, intervals, last done, next due, remaining); **Mark done** card; **History** card (completions, most recent first, or an empty message); actions **Edit**, **Pause** or **Resume**, and **Delete** (a `details` confirm panel like vehicle delete). Not found shows the standard not-found message with a link back to the vehicle's Reminders tab.

### Mark done (form `complete-reminder`)

Fields: Done on (default today), Mileage (default the vehicle's current mileage; required when the reminder has a distance interval), and a checkbox "Also update the vehicle's mileage" that is always shown; the server raises the vehicle's latest odometer reading only when the mileage is above the vehicle's current mileage and never lowers it. Success redirects to the detail page with `saved=done` and a status notice "Marked done. Next due ...".

### Pause, Resume, Delete

Small post forms (`pause-reminder`, `resume-reminder`, `delete-reminder`) redirect with `saved=paused`, `saved=resumed`, and (for delete) to the vehicle's Reminders tab with `saved=deleted`. Delete requires the confirm panel.

## `GET /vehicles/{id}/reminders/{reminderId}/edit` and `POST` (form `edit-reminder`)

The same form as create, pre-filled. Success redirects to the reminder's detail page with `saved=updated`.

## `GET /reminders` (garage-wide)

Page header "Reminders". Optional query `status` (`overdue`, `due-soon`, `upcoming`, `paused`, `mileage-needed`; anything else means all). A filter row of links shows each status with its count from the loaded list. The list shows the same rows as the vehicle tab plus the vehicle name linking to the vehicle, overdue first, then due soon, then the rest. Empty state: explanation and a link to My Vehicles ("Add a reminder from a vehicle's Reminders tab").

## `GET /dashboard`

Page header "Dashboard". An empty garage (no vehicles) shows every figure as zero and the empty-state invitation **Add your first vehicle**.

| Block | Content |
|---|---|
| Metric cards (Bootstrap `row-cols`) | Vehicles (links to `/vehicles`), Service records (no link), Fuel and charging entries (no link), Drafts awaiting review (links to `/drafts`), Reminders overdue (links to `/reminders?status=overdue`), Reminders due soon (links to `/reminders?status=due-soon`). Each card has a visible label and number; the overdue card uses the danger color and the due-soon card warning, both with text. |
| Needs attention | Up to five reminders, overdue first, each with its vehicle name, status badge, remaining amount, and a link to the reminder. For a garage with vehicles and nothing due it shows "Nothing needs attention right now."; for an empty garage the section is absent because the first-vehicle invitation is shown instead. |

| Fuel and charging spend | For a garage with vehicles: a grouped bar chart of cost per month for the last 12 months, fuel and charging as separate bars, drawn as one inline SVG (no chart library) with a legend carrying each series' 12-month total, a text title, a tooltip on every bar, and a "Show the figures" table of the same values. When nothing was spent in those months it shows "No fuel or charging costs in the last 12 months" instead of an empty chart. Absent for an empty garage. |

New metrics later add cards to this page; existing cards and the Api fields behind them do not change.

## Failure handling

All pages use `ApiFailureAlert` for sign-in-again, unavailable, and not-accepted answers. A 404 from the Api for a vehicle or reminder shows the standard not-found message and sets the response status to 404.

## Messages (`saved` query values, digits and letters only, anything unknown is ignored)

`created`, `updated`, `done`, `paused`, `resumed`, `deleted` map to short status notices (`role="status"`).

## Non-goals

No push, email, or text alerts; no charts; no editing of the suggested types; no link between a reminder and a service record or a registration record.
