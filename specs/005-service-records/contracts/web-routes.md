# Contract: Web Routes (Vehicle Service Records)

All routes require a signed-in user (the existing fallback authorization policy). Pages are static SSR (`[ExcludeFromInteractiveRouting]`) and call the API with the user's token through a typed `ServiceRecordsApiClient`. Mutations are antiforgery-protected form posts followed by a redirect (post-redirect-get). Everything not mentioned here is unchanged from features [002](../../002-vehicle-tracking/contracts/web-routes.md) and [004](../../004-expanded-vehicle-details/contracts/web-routes.md).

## Changed: vehicle detail `/vehicles/{id:guid}`

A fourth tab, **Maintenance** (icon: wrench; id `service-records`), holding a three-item accordion (Service Records, Inspections and Fuel Data placeholders),, is added to the shared tab strip after Description. `?tab=service-records` opens it; the list pages with `recordsPage`, read as text so a bad value means page 1 (like `notesPage`).

| Part | Content |
|---|---|
| Summary (only when records exist) | "N service records" and "Last serviced {date}" (the first record of page 1). |
| Action | "Add service record" button linking to `/vehicles/{id}/service-records/new`; shown in the empty state too. |
| List | One Bootstrap card/list-group row per record, most recent Date Serviced first: date, odometer ("12,345 mi"), shop or person ("Not set" when empty), "N line items", total cost; the row links to the record page. Not a wide table, so it works on a phone. |
| Empty state | `EmptyState` "No service records yet" with the add action. |
| Paging | Existing `Pager`, 10 per page, `HrefFor` keeping `tab=service-records`. |
| Failure | `ApiFailureAlert`; the rest of the page stays usable. |

The delete-vehicle confirmation reads "This also removes its tags, journal notes, service records, and pictures and cannot be undone."

The **Details** tab (Basic group) gains **Latest Odometer Reading** (after the existing mileage field; "Not set" when empty). The add and edit forms gain the same field in Basic Info, with helper text: "Shown on your garage card unless a newer service record has a higher reading."

## New pages

| Route | Purpose | Notes |
|---|---|---|
| `/vehicles/{id:guid}/service-records/new` | Add a service record | Fields below. On success redirects to the new record page so line items can be added; a 404 for the vehicle shows the standard not-found state. |
| `/vehicles/{id:guid}/service-records/{recordId:guid}` | View a record | Metadata as a definition list; line item list; total cost; actions. |
| `/vehicles/{id:guid}/service-records/{recordId:guid}/edit` | Edit a record's metadata | Same fields as add. Success redirects to the record page. |
| `/vehicles/{id:guid}/service-records/{recordId:guid}/line-items/new` | Add a line item | Fields below. Success redirects to the record page. |
| `/vehicles/{id:guid}/service-records/{recordId:guid}/line-items/{lineItemId:guid}/edit` | Edit a line item | Same form, prefilled; replaces pricing and parts. |

Unknown or foreign ids render the not-found state with a 404 status and a link back to the vehicle's Service Records tab. Page header trail: Vehicles > {Year Make Model} > Service record {date}.

### Add and edit record form

| Field | Control | Rules |
|---|---|---|
| Date serviced* | date input | Not in the future. |
| Odometer reading* | number input, `inputmode="numeric"`, "mi" suffix | 0-9,999,999. |
| Shop name or person | text input | Max 100 characters; label notes "or the person who did the work". |

Form names: `create-service-record`, `update-service-record`. Validation messages are linked with `aria-describedby`; a rejected save re-renders the form with messages beside each field.

### Record page

- **Metadata**: Date serviced, Odometer reading, Shop name or person ("Not set"), plus Edit and Delete actions. Delete uses the `<details>` confirmation panel ("Delete this service record? This also removes its line items and cannot be undone.") with form `delete-service-record`; success redirects to `/vehicles/{id}?tab=service-records`.
- **Line items**: heading "Line items" with an "Add line item" link. Each item shows its description, a pricing summary (flat cost; or "Labor: 1.5 h x $90.00 = $135.00" and a parts list "2 x Oil filter at $12.50 = $25.00"), the line total, and Edit and Delete actions (row-level `btn-sm`). Delete uses the confirmation panel with form `delete-line-item-{lineItemId}`; redirects back to the record page. Empty state: "No line items yet. Add what was done and what it cost."
- **Total cost**: a prominent "Total cost" figure under the list, the sum of all line item totals (`$0.00` with none).

### Add and edit line item form

| Field | Control | Rules |
|---|---|---|
| What was done* | text input | 1-200 characters. |
| Pricing | radio pair: "Flat cost" / "Labor and parts" | Defaults to Flat cost on add; edit selects the item's current mode. Both groups are always rendered so the form works without JavaScript; the group that does not apply is ignored on save. |
| Flat cost | number input, `step="0.01"` | 0-9,999,999.99. |
| Labor hours / Hourly rate | two number inputs | Required together; hours 0.01-999.99, rate 0-99,999.99. |
| Parts | rows of Part name / Quantity / Price per part | Existing parts plus three blank rows; "Add more part rows" (a submit button named `add-part-rows` inside the same form; the handler sees it in the posted form and re-renders without saving) adds three more blank rows (up to 25); all-blank rows are ignored. |

Form names: `create-line-item`, `update-line-item`. The pricing mode that is not selected is discarded on save (a flat cost is cleared when "Labor and parts" is chosen and vice versa). Messages for API errors map as: `400` re-renders beside the fields (part errors name the row, for example "Part 2: Quantity must be between 0.01 and 9,999.99."); `409` shows "A service record can have up to 50 line items."; `404` shows the not-found state.

## Accessibility and presentation

Bootstrap 5.3 components only; every input and select has a label; validation messages are linked with `aria-describedby`; icon-only controls have accessible names; user-entered text (shop, descriptions, part names) is rendered encoded (no `MarkupString`); totals use `$` with two decimals and thousands separators (invariant culture); no new JavaScript is added.
