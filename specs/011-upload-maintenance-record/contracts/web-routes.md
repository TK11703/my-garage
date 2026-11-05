# Web Routes and UI Contract: Upload a Maintenance Record

All pages are statically rendered (no interactive circuit) and require sign-in. Follow `docs/ui-standard.md`; `UiStandardConformanceTests` scans the new page.

## Entry points

| Where | Control | Target |
|---|---|---|
| Main screen (`/`), hero button row | Link button **Add Maintenance** (plus icon). Shown whether or not the user has vehicles. | `/drafts/upload` |
| Vehicle page, Maintenance tab, above the accordion (`MaintenanceView`) | Link button **Import Record** (upload icon) | `/drafts/upload?vehicleId={vehicleId}` |

## `GET /drafts/upload`

| Query | Meaning |
|---|---|
| `vehicleId` (optional guid) | Preselects that vehicle. Read as text; a missing or malformed value means no vehicle. The vehicle's name is looked up through the Api and shown ("Importing a record for {name}"). A vehicle the Api reports as not found shows the standard not-found message instead of the form. |

Content: page header with a trail (Maintenance Drafts > Add maintenance), short explanation (PDF, JPG, PNG, or WebP; up to 5 files, 10 MB each, 15 MB in total; "Nothing becomes a record until you review and include it"), then one form with a standard Bootstrap file input (so the chosen file names stay visible without scripting):

- `<form method="post" enctype="multipart/form-data" @formname="upload-record">` with antiforgery token.
- A visually hidden label and a `multiple` file input named `recordFiles`, `accept` limited to the four types, `required`, with described-by help text.
- A hidden `vehicleId` field only when preselected.
- Submit button **Upload** with busy text, plus a note that reading happens after upload.

## `POST /drafts/upload` (form `upload-record`)

The page handler reads the posted files and, before any call, refuses: no file ("Choose at least one file"), more than 5 files, a file over 10 MB, or more than 15 MB in total. Otherwise it streams the files to `POST /api/drafts/uploads` through `DraftsApiClient` (the user's own file names are not sent to the Api).

| Api answer | Page result |
|---|---|
| 202, nothing refused | Redirect to `/drafts?uploaded={acceptedCount}` |
| 202, some refused | Stay on the page (no redirect): a status notice says how many files were uploaded and are being read, with a link to the drafts list, and an alert names each refused file (escaped) with its reason |
| 400 (nothing accepted) | Stay on the page; alert listing each refused file by its own name (escaped) and reason |
| 404 (vehicle) | Not-found message with a link back to the vehicle list |
| 413, or the handler's own size checks | Alert naming the limit |
| 429 | Alert: too many uploads, try again in a minute |
| 503 / outage | The standard unavailable alert with a retry link |
| 401 / 403 | The standard sign-in-again alert (`ApiFailureAlert`) |

Reasons shown: unsupported type ("must be a PDF, JPG, PNG, or WebP"), empty, too large. Each alert uses `role="alert"`; the success notice uses `role="status"`.

## Changes to existing pages

| Page | Change |
|---|---|
| `/drafts` | Optional notice from `uploaded` (digits only, clamped; anything else is ignored): "N file(s) uploaded. They are being read; each draft appears below and in your notifications when ready." Empty-state text also mentions uploading. Each draft row and history row shows a small **Uploaded** label when its origin is upload. A link **Add maintenance** to `/drafts/upload` sits in the page header actions. |
| `/drafts/{id}` | For an upload draft the Original card shows an "Uploaded" block (time and file name, escaped) instead of From and Subject; the note for a draft without a file stays for text-only email drafts. The vehicle explanation for `selected` reads "You chose this vehicle when you uploaded the file." |
| `/drafts/{id}/attachment` | Unchanged. |

## Non-goals

No drag-and-drop JavaScript beyond the existing dropzone helper, no interactive upload progress, no change to the record badge wording.
