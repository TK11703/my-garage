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

A card titled "Reminders" with the subtitle "Recurring items for this vehicle, by miles, by time, or both", holding the vehicle's reminders, most urgent first, as flush list rows. Each row has a tile with the next due month and day (a mileage icon for a reminder due by miles only; red overdue, amber due soon, green upcoming, blue mileage needed, gray paused), the name linking to the reminder, what it applies to with its interval ("Equipment: Trailer hitch · Every 1 year"), the last done and next due values, the remaining amount ("1,200 miles left", "due in 18 days", "400 miles overdue") with the part at or past its threshold emphasized for a reminder with both intervals, and a status badge on the right (Overdue danger, Due soon warning, Upcoming secondary, Mileage needed info, Paused light). **Add reminder** is not a button on the tab: it is an item under a "Reminders" heading in the page bar's **Add** dropdown and links to `/vehicles/{id}/reminders/new`. An empty list shows the standard empty state explaining reminders, with an Add reminder link. Status is never conveyed by color alone: the badge always has text.

## `GET /vehicles/{id}/reminders/new` and `POST` (form `create-reminder`)

| Query | Meaning |
|---|---|
| `preset` (optional) | `oil-change`, `tire-rotation`, `inspection`, `registration`, or `custom`. Fills the form from `ReminderPresets`; any other value means custom. |

Form fields: Type (preset links above the form, with Custom), Name, Applies to (radio group: Vehicle, Equipment, Location), Equipment or location name (shown for Equipment and Location; the field is always in the markup and is validated server-side, since no script runs), Repeat every miles, Repeat every (number) and unit (Months or Years), Last done on (defaults to today), Last done mileage (defaults to the vehicle's current mileage). A non-blocking warning appears above the form when the vehicle already has a reminder with the same name. If the vehicle has no recorded mileage, a note says a distance-based reminder cannot show a status until it has one. Validation messages name each field and keep the typed values (FR-020). Save and Cancel sit in a row below the form card, not in its footer (the add and edit forms alike). On success: redirect to `/vehicles/{id}?tab=reminders&saved=created`.

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
| Metric cards (compact, `row-cols-2 row-cols-lg-4`) | Vehicles (the label links to `/vehicles`), Service records (no link), Fuel and charging entries (no link), Drafts awaiting review (the label links to `/drafts`). Each card is a small card with a colored number over a label; the drafts number is colored only when there are drafts. Overdue and due-soon counts are not shown as cards: the Needs attention card lists those reminders. |
| Needs attention | A full-width card titled "Needs attention" with the subtitle "Reminders that are overdue or due soon" and a "View all reminders" link to `/reminders`. Up to five rows, overdue first. Each row has a tile with the next due month and day (a mileage icon for a reminder due by miles only; red tile for overdue, amber for due soon), the reminder name linking to it, the vehicle name linking to the vehicle's reminders, the remaining amount, and a status badge. For a garage with vehicles and nothing due it shows "Nothing needs attention right now."; for an empty garage the section is absent because the first-vehicle invitation is shown instead. |

| Fuel and charging spend | For a garage with vehicles, in a card in the left half (`col-lg-6`): two bar charts for the last 12 months, one for fuel and one for charging (each with its own axis and its 12-month total), drawn as inline SVG (no chart library). Each month's bar is stacked by vehicle; the vehicle that spent the most sits at the bottom and gets the first color, up to seven vehicles have their own color from the theme palette (light and dark), and any more share one gray "Other vehicles" layer. A legend names the vehicles, every chart has a text title, every layer has a tooltip (month, kind, vehicle, cost), and "Show the figures" lists the same numbers per month and vehicle in a table. When nothing was spent in those months it shows "No fuel or charging costs in the last 12 months" instead of empty charts. Absent for an empty garage. |
| Recent updates | A card in the right half (`col-lg-6`, beside the spend card) titled "Recent updates" holding a "Recent activity" list: the last 10 journal entries across the user's vehicles, newest first. Each row has a round avatar (the vehicle's main picture, or a car icon), the vehicle name linking to its Description tab followed by "added a note" (or "added a *Category* note"), the start of the note on one line, and how long ago it was written. With no entries it says how to add one. Absent for an empty garage. |

New metrics later add cards to this page; existing cards and the Api fields behind them do not change.

## Failure handling

All pages use `ApiFailureAlert` for sign-in-again, unavailable, and not-accepted answers. A 404 from the Api for a vehicle or reminder shows the standard not-found message and sets the response status to 404.

## Messages (`saved` query values, digits and letters only, anything unknown is ignored)

`created`, `updated`, `done`, `paused`, `resumed`, `deleted` map to short status notices (`role="status"`).

## Non-goals

No push, email, or text alerts; no charts; no editing of the suggested types; no link between a reminder and a service record or a registration record.
