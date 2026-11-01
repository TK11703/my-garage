# Contract: Web Routes (Vehicle Tracking)

All routes require a signed-in user (the existing fallback authorization policy). Pages are static SSR (`[ExcludeFromInteractiveRouting]`) and call the API with the user's token through the typed API client. Mutations are antiforgery-protected form posts followed by a redirect (post-redirect-get). Unknown or foreign vehicle ids render the standard not-found page.

## Pages

| Route | Purpose | Query | Notes |
|---|---|---|---|
| `/vehicles` | Garage: search/filter bar and paged card grid | `q`, `make`, `color`, `doors`, `tag`, `minMileage`, `maxMileage`, `page` (default 1) | 12 cards per page; non-numeric or `< 1` page -> 1; beyond last -> last page. Search/filter is a GET form (bookmarkable URLs, no antiforgery token). Empty garage shows "Add vehicle" state; criteria with no results show "No vehicles match" with "Clear all". Pager links keep all criteria. |
| `/vehicles/new` | Add vehicle form | | Fields: nickname, make, model, trim, color, mileage, doors. On success redirects to the new vehicle. Validation messages are shown per field. |
| `/vehicles/{id:guid}` | Vehicle detail | `notesPage` (default 1) | Sections: details, tags (add/remove), journal (add form with free-text category input, paged list newest first, edit/delete), image gallery (upload, set main, delete). |
| `/vehicles/{id:guid}/edit` | Edit vehicle form | | Same fields as add; cancel returns to detail. |

## Garage search and filters

A search box (`q`) and a collapsible "Filters" panel (Bootstrap collapse, open when any filter is active) sit above the cards. Filter controls: make (select), color (select), doors (select 1-6), tag (select), minimum mileage and maximum mileage (number inputs). Select choices come from the API's filter-options endpoint (only values in the user's garage). "Apply" submits the GET form (resets `page`); active criteria are displayed as badges, each a link that removes just that criterion, plus a "Clear all" link. Invalid input (for example min > max) shows an inline validation message and keeps the page usable. A result summary (for example "7 vehicles") is announced in a polite live region.

## Card (garage item)

A Bootstrap card per vehicle: image (or placeholder) at the top, then nickname (title, links to the detail page), `make model`, and mileage (formatted with thousands separators and "mi"; omitted text such as "Mileage not set" when absent). The image has `alt` text of the nickname and `loading="lazy"`. The whole card title is a focusable link; no table markup is used.

## Form handlers (named forms on the pages above)

| Form name | Page | Result |
|---|---|---|
| `create-vehicle` | `/vehicles/new` | POST to API `createVehicle`; on 400 shows messages; on success redirect. |
| `update-vehicle` | `/vehicles/{id}/edit` | `updateVehicle`. |
| `delete-vehicle` | detail (confirm modal) | `deleteVehicle` then redirect to `/vehicles`. |
| `add-tag` / `remove-tag-{tagId}` | detail | `addVehicleTag` (409 -> "already exists"/"limit reached") / `removeVehicleTag`. |
| `add-note` / `edit-note-{noteId}` / `delete-note-{noteId}` | detail | `createJournalNote` / `updateJournalNote` / `deleteJournalNote`; the category is an optional text input (max 40 characters), blank means uncategorized. |
| `set-main-{imageId}` / `delete-image-{imageId}` | detail | `setMainVehicleImage` / `deleteVehicleImage`. |

## Endpoints (non-page)

| Method and route | Purpose | Behavior |
|---|---|---|
| `POST /vehicles/{id:guid}/images` | Receive a picture upload | `multipart/form-data` (`file`, optional `caption`), antiforgery required, size limit about 5.5 MB; streams to API `uploadVehicleImage`; redirects to the vehicle with a success/failure message. |
| `GET /vehicles/{id:guid}/images/{imageId:guid}` | Serve an image to `<img>` | Requires sign-in cookie; calls API `getVehicleImageContent` with the user's token (the API streams from private blob storage); streams bytes; sets `Content-Type` from the API, `X-Content-Type-Options: nosniff`, `Cache-Control: private, max-age=86400`; 404 passes through; if storage is unavailable (API 503) the endpoint returns 503 and the browser shows the image's `alt` text, while the rest of the page is unaffected. |

## Navigation

`NavMenu` gains a "My garage" link to `/vehicles`. The home page links to the garage.

## Accessibility and presentation

Bootstrap 5.3 components only (grid `row-cols-1 row-cols-sm-2 row-cols-lg-3`, cards, badges, pagination, modal via data attributes); labels on all inputs; pager is a `nav` with `aria-label`; icon-only buttons have accessible names; user text is rendered encoded (no `MarkupString`); animations respect `prefers-reduced-motion`.
