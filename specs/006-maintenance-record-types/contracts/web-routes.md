# Contract: Web Routes (Maintenance Records)

All routes require a signed-in user (the existing fallback authorization policy). Pages are static SSR (`[ExcludeFromInteractiveRouting]`) and call the API with the user's token through typed clients (one per feature). Mutations are antiforgery-protected form posts followed by a redirect (post-redirect-get). Everything not mentioned here is unchanged from features [002](../../002-vehicle-tracking/contracts/web-routes.md), [004](../../004-expanded-vehicle-details/contracts/web-routes.md), and [005](../../005-service-records/contracts/web-routes.md).

## Changed: vehicle detail `/vehicles/{id:guid}`, Maintenance tab (`?tab=service-records`)

The tab keeps its accordion; the Inspections and Charging and Fueling placeholders are replaced and the items reordered. Items open and close independently.

| Accordion item | Heading | Content |
|---|---|---|
| 1. Service Records | "Service Records (N)" and "Last Service {date}" | Unchanged: add button, list, pager (`recordsPage`). |
| 2. Charging and Fueling | "Charging and Fueling (N)" and "Last entry {date}" | Buttons "Add fuel" (`/fuel-energy-entries/new?kind=fuel`) and "Add charge" (`?kind=energy`); list; pager (`fuelPage`). |
| 3. Inspections, Registrations, Insurance, and Taxes | "Inspections, Registrations, Insurance, and Taxes" and a muted line "N inspections, N registrations, N insurance, N taxes" | Four stacked subsections, each with its own subheading, add button, list, empty state, and pager (`inspectionsPage`, `registrationsPage`, `insurancePage`, `taxesPage`), in the order Inspections, Registrations, Insurance, Taxes. |

**Which items start open**: `?section=records` (default, also when absent or unrecognized), `?section=fuel`, or `?section=compliance`. Create, edit, and delete pages redirect to `/vehicles/{id}?tab=service-records&section=<value>` (adding `<kind>Page` only when a page other than the first is being returned to). Page query values are read as text, so a bad value means page 1.

### List rows (all five new lists)

One Bootstrap list-group row per entry (not a wide table, so it works on a phone). Each row shows every value of the entry, user text encoded, and an **Edit** link (icon and text; accessible name "Edit {kind} from {date}"). Empty state (`EmptyState`, compact) names the kind and repeats the add action. Failure uses `ApiFailureAlert`; the rest of the tab stays usable.

| List | Row content (left to right, wrapping) |
|---|---|
| Charging and Fueling | Date; a "Fuel" or "Charge" badge; "{quantity} gal at ${price}/gal" or "{quantity} kWh at ${price}/kWh"; total (`$1,234.50`). |
| Inspections | Date; "Safety" or "Emissions"; cost; "Next due {date}" or "Next due Not set". |
| Registrations | State name and code; cost; "Expires {date}". |
| Insurance | "{start date} to {end date}"; cost with its payment frequency (for example "$120.00 Monthly", "$640.00 Bi-Annual", "$1,240.00 Annual"). |
| Taxes | Type label ("Personal Property", "Lease", "Purchase"); amount; recurrence label ("Once", "Monthly", "Bi-Annual", "Annual"). |

Unit prices carry a `$` prefix and show at least two and at most four decimals, trailing zeros trimmed beyond two (for example `$3.459`, `$0.1342`, `$4.00`). Quantities show up to three decimals, trailing zeros trimmed. Dates use the existing "MMM d, yyyy" format; money uses `$` with two decimals (invariant culture).

The delete-vehicle confirmation reads "This also removes its tags, journal notes, service records, charging and fueling entries, inspections, registrations, insurance, taxes, and pictures and cannot be undone."

## New pages

| Route | Purpose | Notes |
|---|---|---|
| `/vehicles/{id:guid}/fuel-energy-entries/new` | Add a fuel or charging entry | `?kind=fuel` (default) or `?kind=energy`, read as text. |
| `/vehicles/{id:guid}/fuel-energy-entries/{entryId:guid}/edit` | Edit and delete an entry | Prefilled; the kind is shown as read-only text. |
| `/vehicles/{id:guid}/inspections/new` and `/inspections/{entryId:guid}/edit` | Add; edit and delete | |
| `/vehicles/{id:guid}/registrations/new` and `/registrations/{entryId:guid}/edit` | Add; edit and delete | |
| `/vehicles/{id:guid}/insurance/new` and `/insurance/{entryId:guid}/edit` | Add; edit and delete | |
| `/vehicles/{id:guid}/taxes/new` and `/taxes/{entryId:guid}/edit` | Add; edit and delete | |

Unknown or foreign ids render the not-found state with a 404 status and a link back to the Maintenance tab. Page header trail: Vehicles > {Year Make Model} > Add fuel (or Edit fuel entry, Add charge, Add inspection, and so on). On success the page redirects as described above.

### Forms

Validation messages are linked with `aria-describedby`; a rejected save re-renders the form with messages beside each field. Required fields are marked with an asterisk. Form names: `create-<kind>`, `update-<kind>`, `delete-<kind>` where `<kind>` is `fuel-energy-entry`, `inspection`, `registration`, `insurance-policy`, or `tax`.

**Fuel entry** (kind `fuel`)

| Field | Control | Rules |
|---|---|---|
| Date* | date input | Not in the future. |
| Price per gallon* | number input, `step="0.0001"` | 0-9,999.9999. |
| Total gallons* | number input, `step="0.001"` | 0.001-99,999.999. |
| Total | number input, `step="0.01"` | Optional; helper text "Leave empty to calculate it from the price and gallons." |

**Charging entry** (kind `energy`): the same fields labeled "Date", "Price per kWh", "kWh delivered", and "Total session cost" (helper text "Leave empty to calculate it from the price and kWh.").

**Inspection**

| Field | Control | Rules |
|---|---|---|
| Type* | select: Safety, Emissions | Required. |
| Date* | date input | Not in the future. |
| Cost* | number input, `step="0.01"` | 0-9,999,999.99. |
| Next due date | date input | Optional; not before the date. |

**Registration**

| Field | Control | Rules |
|---|---|---|
| State* | select of the 50 states and DC, option text "Washington (WA)" | Required. |
| Cost* | number input, `step="0.01"` | 0-9,999,999.99. |
| Expiration date* | date input | 1900-01-01 to 2100-12-31. |

**Insurance**

| Field | Control | Rules |
|---|---|---|
| Start date* | date input | 1900-01-01 to 2100-12-31; may be in the past. |
| End date* | date input | 1900-01-01 to 2100-12-31; later than the start date. |
| Duration* | select: Monthly, Bi-Annual (twice a year), Annual | Required; helper text "How often the premium is paid." |
| Cost* | number input, `step="0.01"` | 0-9,999,999.99; helper text "The premium paid each time, at the duration above." |

**Tax**

| Field | Control | Rules |
|---|---|---|
| Type* | select: Personal Property, Lease, Purchase | Required. |
| Amount* | number input, `step="0.01"` | 0.01-9,999,999.99. |
| Recurrence* | select: Once, Monthly, Bi-Annual (twice a year), Annual | Required. |

### Edit page delete

Below the form, a `<details>` confirmation panel ("Delete this {kind}? This cannot be undone.") posts `delete-<kind>`; Cancel closes the panel. Success redirects to the Maintenance tab with the section open. A 404 shows the not-found state.

## Accessibility and presentation

Bootstrap 5.3 components only; every input and select has a label; icon-only controls have accessible names; user-entered text is rendered encoded (no `MarkupString`); the new accordion item headings are real buttons with `aria-expanded` and `aria-controls`; the Maintenance tab and its forms work at phone width without horizontal page scrolling; no new JavaScript is added.
