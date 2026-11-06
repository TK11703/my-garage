# Documentation

Use this folder for project documentation, including onboarding guides, architecture decisions, requirements, and operational runbooks.

Keep documentation current as implementation and workflows evolve.

## Contents

- [UI standard](ui-standard.md): logo, palette and light/dark themes, page frame (top bar, left menu, page bar), breadcrumb and title rules, and component patterns for every page, including the notifications menu, the email drafts pages, and the compatibility panel.
- [Theme preview](theme-preview.html): open in a browser to see the light and dark themes, the page frame at desktop, tablet, and phone widths, color swatches, and sample cards, tabs, accordions, and forms. It loads the web application's own stylesheet.

## Features

Each feature is specified under [`specs/`](../specs) (spec, plan, research, data model, contracts, quickstart, tasks).

| Spec | Feature |
|---|---|
| `001-app-foundation-auth` | App shell and Microsoft Entra sign-in |
| `002-vehicle-tracking` | Private vehicle list per user |
| `003-ui-branding-standards` | Logo, palette, page frame ([UI standard](ui-standard.md)) |
| `004-expanded-vehicle-details` | Tags, journal notes, and vehicle pictures |
| `005-service-records` | Service records with line items and parts |
| `006-maintenance-record-types` | Fuel and charging, inspections, registrations, insurance, and taxes |
| `007-email-ingestion-api` | Email ingestion: a trusted workflow hands over forwarded emails, the app proposes draft records, and the owner reviews them |
| `008-github-cicd-pipeline` | GitHub Actions CI/CD: verification on every pull request, automatic deployment to the shared Azure Container Apps environment, Bicep, one-time setup scripts, and the email-intake Logic App script (see [infra/README.md](../infra/README.md)) |
| `011-upload-maintenance-record` | Upload a maintenance record: a file is queued through the email draft pipeline, from the main screen or with a vehicle preselected |

### Email ingestion (feature 007)

- **Intake**: an external workflow (a Logic App) calls `POST /api/intake/emails` with an Entra app-only token that carries the `Email.Ingest` app role. The endpoint finds the owner from the sender address, stores the email and its PDF and image attachments, and answers `202` before any processing. See [infra/README.md](../infra/README.md) for the workflow contract.
- **Processing**: a background service reads each queued submission with Azure OpenAI and creates draft maintenance items (never records) for the matched owner. Drafts wait 30 days for review, then expire.
- **Review**: the owner opens **Email drafts** from the notifications menu, checks the compatibility panel, and saves a draft as a record (optionally keeping the original file under the vehicle's Documents) or discards it. **History** lists what happened to each email.
- **Privacy**: logs, metrics, and failure reasons carry ids, counts, and statuses only; email text, addresses, file names, and extracted values are never logged. Attachments live in private blob containers (`email-intake`, `vehicle-documents`) and are streamed to the owner through the web app.

### Uploading a record (feature 011)

- **Upload**: **Add Maintenance** on the main screen, or **Import Record** in a vehicle's Maintenance tab (which proposes that vehicle), opens `/drafts/upload`. Up to 5 PDF or image files (10 MB each, 15 MB in total) are posted to `POST /api/drafts/uploads` as the signed-in user.
- **Same pipeline**: each upload is queued as a submission of origin `upload` and read by the same background service as email attachments, so the drafts, review, keep-original option, expiry, and history are shared. Records created this way carry the same from-email mark. `DraftUploads:RequestsPerMinute` limits uploads per user.

### Service reminders and the dashboard (feature 013)

- **Reminders**: each vehicle has a **Reminders** tab. A reminder repeats by miles, by months or years, or both (an oil change every 5,000 miles or 6 months, an inspection or registration every year), applies to the vehicle, to equipment on it, or to the place it is kept, and is due at whichever limit comes first. **Mark done** starts the next cycle from the date and mileage you enter and keeps a history; a reminder can also be edited, paused, resumed, or deleted. **Reminders** in the left menu lists every reminder across the garage, most urgent first.
- **Status**: derived on every read, never stored. Due soon is within 500 miles or 30 days of a due point; Overdue is reached or passed; a distance-only reminder on a vehicle with no mileage shows Mileage needed. The vehicle's mileage is the same one the garage cards show.
- **Notifications**: the bell raises one notification when a reminder becomes due soon and one when it becomes overdue. The checks run when a reminder, the vehicle's mileage, or a service record is saved, and hourly in the background (`Reminders:Sweep:Enabled`, `Reminders:Sweep:IntervalMinutes`).
- **Dashboard**: **Dashboard** in the left menu shows counts of vehicles, service records, fuel and charging entries, drafts awaiting review, and overdue and due-soon reminders, plus the reminders that need attention. More metrics are added to it as they become available.
