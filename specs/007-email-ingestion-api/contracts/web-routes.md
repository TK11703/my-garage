# Web Routes and UI Contracts: Email Ingestion Intake

New pages and behavior in `MyGarage.Web`. All routes require sign-in (existing fallback policy) and follow [docs/ui-standard.md](../../../docs/ui-standard.md): `PageHeader`, `EmptyState`, `ApiFailureAlert`, Bootstrap 5.3 only, accessible names on icon buttons.

## Pages

| Route | Page | Purpose |
|---|---|---|
| `/drafts` | `DraftsListPage` | Drafts awaiting review, newest first, paged 10 at a time, with a "History" tab listing what happened to each email (`/api/emails`). Empty state: "No drafts waiting. Forward a service invoice or receipt to your My Garage mailbox." |
| `/drafts/{draftId:guid}` | `DraftReviewPage` | Review one draft (below). Unknown or not-yours draft id shows the standard not-found page. |

## Draft review page

Two columns at desktop width, stacked on a phone:

1. **Original** (left): the attachment in a same-origin, titled `<iframe>` without a `sandbox` attribute (PDF; Chrome's PDF viewer does not render in a sandboxed frame) or an `<img>` (image) from `/drafts/{id}/attachment`, with a "Download" link and the email's sender, subject, and arrival time (rendered escaped). Text-sourced drafts show the retained email text in a `<pre>` with escaping.
2. **Proposal** (right):
   - Record type selector (the seven kinds). Changing it calls `PUT /api/drafts/{id}/kind` and re-renders the form.
   - Vehicle picker listing only the user's vehicles, preselected from the suggestion with a note on how it was matched ("matched by VIN", "only vehicle", "choose a vehicle").
   - The kind's form, prefilled. For a service record: date serviced, odometer, shop or person, and a line-item editor (description, flat cost or labor hours and rate, parts with name, quantity, unit price, running totals per line and overall) built from the same field rules as the manual service-record pages.
   - **Compatibility panel** (above the form, `aria-live="polite"`): a summary "Ready to save" or "N things need attention", then per field `ok` / `missing` / `invalid` with the message and a link that focuses the field; warnings such as "No line items found", "Line totals differ from the invoice total", "This file looks like one you already received". Recalculated through `POST /api/drafts/{id}/compatibility` (debounced) as the user edits.
   - "Keep the original file with this record" checkbox (shown only when an attachment exists), explaining it is stored privately under Documents, apart from vehicle pictures.
   - Actions: **Save record** (accept; disabled while `isAcceptable` is false) and **Discard** (inline confirmation, no modal script). Successful save returns to the vehicle's Maintenance tab with the relevant accordion item open and a confirmation message; discard returns to `/drafts`.
   - Expired or already decided drafts show a read-only notice and a link back to the list.

## Same-origin proxy routes (minimal endpoints in Web)

| Route | Source | Notes |
|---|---|---|
| `GET /drafts/{draftId:guid}/attachment` | Api `GET /api/drafts/{id}/attachment` | Requires sign-in; always sets `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`, and `Content-Disposition: inline`. PDFs also get `X-Frame-Options: SAMEORIGIN` and no `Content-Security-Policy` (Chrome's PDF viewer does not render under `sandbox`; the viewer runs in an isolated process and the file is the owner's own attachment); images get `Content-Security-Policy: sandbox`. |
| `GET /vehicles/{vehicleId:guid}/documents/{documentId:guid}` | Api `GET /api/vehicles/{vehicleId}/documents/{documentId}/content` | Same headers; used by the Documents section. |

## Navigation bar

- `NotificationsMenu` replaces the static bell: a badge with the unread count (hidden at zero, with `aria-label` "Notifications, N unread"), a dropdown of the latest five (title, relative time, link to `/drafts/{id}`), "Mark all as read", and "View all drafts" to `/drafts`. It reads `GET /api/notifications/unread-count` every 60 seconds and after each navigation, and fetches the list when opened. An API failure leaves the bell usable and shows no badge (failures are swallowed as for other non-essential calls).
- The account menu gains "Email drafts" with the awaiting-review count.

## Maintenance tab changes

- New accordion item **Documents** (below the existing sections): list of kept originals (file name, type, size, kept date, "From service record" style source label), view link through the proxy, and delete with confirmation. Empty state: "No documents kept yet." These never appear in the vehicle's image gallery.
- Rows in the six record lists show a small "From email" badge when `FromEmail` is true.

## Accessibility and safety

- The iframe has a `title`; all controls reachable by keyboard; focus moves to the compatibility panel heading after a failed save.
- Email-derived text (sender, subject, notes, extracted descriptions) is always rendered through Blazor's default escaping; no `MarkupString`.
