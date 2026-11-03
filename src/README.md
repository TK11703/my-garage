# Source

Use this folder for application source code and production projects, such as APIs, services, user interfaces, and data-access components.

Organize related code by feature while preserving project boundaries and dependency direction.

| Project | Role |
|---|---|
| `MyGarage.Web` | Blazor UI; reaches data only over HTTP through `MyGarage.Api` |
| `MyGarage.Api` | Minimal APIs, validation, authorization, and the background services of the email feature |
| `MyGarage.Database` | PostgreSQL repositories, SQL scripts (`Scripts/<Feature>/`), forward-only migrations (`0001`-`0012`), and the blob-backed stores for images, email attachments, and kept documents |
| `MyGarage.Shared` | DTOs and contracts only |
| `MyGarage.ServiceDefaults` | Aspire cross-cutting defaults (telemetry, health, logging) |
| `MyGarage.AppHost` | Aspire orchestration: PostgreSQL, Azure Storage (Azurite emulator), API, and web |

## Email ingestion (feature 007)

Feature folders (each exists in `Api`, `Database`, `Shared`, and where there is UI, in `Web`): `EmailIntake` (intake endpoint, submission queue, extraction), `Drafts` (review, compatibility check, accept, discard, history), `Notifications`, `VehicleDocuments` (originals kept with a record), and `Users` (the profile that maps a sender address to an owner).

Blob containers on the existing `blobs` resource, created at runtime: `email-intake` (attachments until their draft is decided or expires) and `vehicle-documents` (originals the user chose to keep, apart from `vehicle-images`).

Hosted services in `MyGarage.Api`:

| Service | Purpose |
|---|---|
| `SubmissionProcessor` | Polls `email_submissions`, claims one due submission at a time, extracts a proposal, and creates drafts and notifications. Retries after 1, 5, then 30 minutes |
| `DraftExpiryService` | Hourly: expires drafts nobody reviewed within 30 days and removes their text, notifications, and stored files (`Drafts:Expiry:Enabled`, `Drafts:Expiry:IntervalMinutes`) |
| `OrphanBlobSweepService` | Hourly: removes intake blobs older than an hour that no attachment row refers to |
| `IntakeContainersHostedService` | Creates the `email-intake` container at startup (the documents and images containers have their own equivalents) |

Configuration read by the API:

| Setting | Meaning |
|---|---|
| `EmailIntake:Extraction:Endpoint` | Azure OpenAI endpoint, for example `https://<resource>.openai.azure.com`. Empty disables extraction: text-only emails end as not applicable and attachments become manual-entry drafts |
| `EmailIntake:Extraction:Deployment` | Name of the model deployment that answers extraction calls |
| `EmailIntake:Extraction:TimeoutSeconds` | Upper bound for one extraction call (default 90) |
| `EmailIntake:Processing:Enabled` | `false` stops `SubmissionProcessor` and `OrphanBlobSweepService` from polling (tests set it to `false` and call them directly; default `true`) |
| `EmailIntake:Processing:PollIntervalSeconds`, `LeaseMinutes` | Idle wait between polls (5) and how long a claimed submission stays leased to one worker (10) |

No key is configured anywhere: the Azure OpenAI and storage calls use `DefaultAzureCredential` (the managed identity in Azure, the developer's sign-in locally).
