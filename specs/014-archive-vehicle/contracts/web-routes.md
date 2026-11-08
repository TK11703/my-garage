# Web Routes and UI Contract: Archive Vehicle

All pages stay statically rendered (no interactive circuit) and require sign-in. Follow `docs/ui-standard.md`; `UiStandardConformanceTests` scans the changed `.razor` files. No JavaScript is added.

## Vehicle list and search (`/vehicles`, `/search`)

| Query | Meaning |
|---|---|
| `status` (optional) | `archived` or `all`. Absent, `active`, or anything else means Active. The default is omitted from generated links. |

- A three-choice link group (Bootstrap nav pills, labeled "Show vehicles") with **Active**, **Archived**, and **All** sits above the results on both pages. The current choice has `aria-current="page"` and the active style; the choices are plain links, so no script is needed.
- Every other criterion and the page number are kept when the status changes (page resets to 1), and the status is kept by paging and by the other filters' links.
- `/vehicles` handles `status` itself and does not forward it to `/search`; `/search` carries it in `VehicleListCriteria` and shows a non-default status as a removable badge ("Status: Archived", "Status: All") like the other criteria.
- An archived vehicle's card (`VehicleCard`, `VehicleSearchCard`) shows a muted **Archived** badge and "Archived {date}" and otherwise looks like any card; it links to the same vehicle page.
- Empty states: the default list with no active vehicles keeps "You have no vehicles yet." when the garage has none at all, and otherwise says "You have no active vehicles." with a link to the Archived choice; the Archived choice with none says "No archived vehicles. Archive a vehicle you no longer own to keep its history without counting it in your garage."

## Vehicle page, active vehicle (`/vehicles/{id}`)

- The page-bar actions gain **Archive**: a `details` confirm panel like Delete (form `archive-vehicle`) reading "Archive {name}? It leaves your garage figures and its reminders are disabled, but all its history is kept and you can restore it any time." with **Yes, archive this vehicle** and **Keep vehicle active**. Success redirects to `/vehicles/{id}?saved=archived`.
- The Delete confirmation text keeps its current list and adds a last sentence: "To keep the history instead, archive the vehicle."

## Vehicle page, archived vehicle (`/vehicles/{id}`)

- A warning alert under the page bar: "This vehicle was archived on {date}. Its history is kept but it cannot be changed, and it is not counted in your garage figures except fuel and charging." with the vehicle's name in the page header suffixed by an **Archived** badge.
- Page-bar actions: **Restore** (a `details` confirm panel, form `restore-vehicle`, "Restore {name}? It returns to your garage and its reminders are enabled again.", success redirects to `/vehicles/{id}?saved=restored`) and **Delete** (unchanged text). **Edit**, the **Add** menu, and **Import Record** are not rendered.
- Every tab shows its content as today but with no add, edit, delete, upload, set-main, pause, resume, mark-done, or import controls. This is done by a cascading `VehicleArchivedState` set once in `VehicleDetail`; sections that render a control wrap it in `@if (!ArchivedState.IsArchived)`.
- Status notices: `saved=archived` and `saved=restored` show a success alert ("Vehicle archived. Find it under Archived in My Vehicles." and "Vehicle restored.").

## Reminders

- A reminder of an archived vehicle (on its Reminders tab and detail page) shows a muted **Archived** status badge with the text "Disabled, vehicle archived" and no remaining-amount line, sorted after Paused. The row keeps its name, interval, and last-done values. Pause, Resume, Mark done, Edit, and Delete are not shown.
- `/reminders` (garage-wide) and the dashboard never list archived-vehicle reminders, so their status links and counts have no Archived choice.

## Dashboard (`/dashboard`)

- Cards, Needs attention, and Recent updates follow the API: archived vehicles are not in the vehicle, service record, or reminder figures or the activity list.
- The fuel and charging entry card and the spend charts include archived vehicles; an archived vehicle's legend entry, tooltip, and "Show the figures" row append " (archived)" to its name.

## Write pages reached by address while the vehicle is archived

Add and edit pages under `/vehicles/{id}/...` (service records, fuel and charging, inspections, registrations, insurance, taxes, reminders, documents) and the vehicle edit page are not linked, but if opened (a bookmark or a stale tab) and the API answers `409` `urn:mygarage:problem:vehicle-archived`, the shared response handling maps it to `ApiOutcome.Archived` and the page shows the standard `ArchivedVehicleNotice`: "This vehicle is archived and cannot be changed." with **Restore this vehicle** and **Back to the vehicle** links. A form post that gets the same answer re-renders the notice and does not lose the user's typed values.

## Draft review (`/drafts/{id}`)

- The vehicle choice offers only active vehicles; a draft whose vehicle was archived arrives with no vehicle selected and the existing "choose a vehicle" prompt. A stale include into an archived vehicle shows the existing unknown-vehicle message.

## Help

The FAQ gains entries: "What is the difference between archiving and deleting a vehicle?", "How do I see or restore an archived vehicle?", and "What does archiving change in the dashboard and reminders?" (the fuel and charging exception stated plainly). The existing dashboard and reminders answers get one sentence each.

## Failure handling

Unchanged: sign-in rejections and outages use the shared `ApiFailureAlert`; not found uses the standard not-found message. A `409` on archive or restore (the vehicle is already in that state in another tab) re-renders the vehicle page in its current state with a status message "This vehicle was already archived." or "This vehicle was already active."
