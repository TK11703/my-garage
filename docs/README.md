# Documentation

Use this folder for project documentation, including onboarding guides, architecture decisions, requirements, and operational runbooks.

Keep documentation current as implementation and workflows evolve.

## Contents

- [UI standard](ui-standard.md): logo, palette, page frame, breadcrumb and title rules, and component patterns for every page, including the notifications menu, the email drafts pages, and the compatibility panel.

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

### Email ingestion (feature 007)

- **Intake**: an external workflow (a Logic App) calls `POST /api/intake/emails` with an Entra app-only token that carries the `Email.Ingest` app role. The endpoint finds the owner from the sender address, stores the email and its PDF and image attachments, and answers `202` before any processing. See [infra/README.md](../infra/README.md) for the workflow contract.
- **Processing**: a background service reads each queued submission with Azure OpenAI and creates draft maintenance items (never records) for the matched owner. Drafts wait 30 days for review, then expire.
- **Review**: the owner opens **Email drafts** from the notifications menu, checks the compatibility panel, and saves a draft as a record (optionally keeping the original file under the vehicle's Documents) or discards it. **History** lists what happened to each email.
- **Privacy**: logs, metrics, and failure reasons carry ids, counts, and statuses only; email text, addresses, file names, and extracted values are never logged. Attachments live in private blob containers (`email-intake`, `vehicle-documents`) and are streamed to the owner through the web app.


