# Source

Use this folder for application source code and production projects, such as APIs, services, user interfaces, and data-access components.

Organize related code by feature while preserving project boundaries and dependency direction.

| Project | Role |
|---|---|
| `MyGarage.Web` | Blazor UI; reaches data only over HTTP through `MyGarage.Api` |
| `MyGarage.Api` | Minimal APIs, validation, authorization |
| `MyGarage.Database` | PostgreSQL repositories, SQL scripts (`Scripts/<Feature>/`), forward-only migrations, and the blob-backed image store |
| `MyGarage.Shared` | DTOs and contracts only |
| `MyGarage.ServiceDefaults` | Aspire cross-cutting defaults (telemetry, health, logging) |
| `MyGarage.AppHost` | Aspire orchestration: PostgreSQL, Azure Storage (Azurite emulator), API, and web |
