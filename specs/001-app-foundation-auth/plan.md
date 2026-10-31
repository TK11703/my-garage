# Implementation Plan: Application Foundation and User Sign-In

**Branch**: `001-app-foundation-auth` | **Date**: 2026-10-31 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-app-foundation-auth/spec.md`

## Summary

Stand up the solution skeleton: a Blazor Web App (`MyGarage.Web`) that signs users in against a single Microsoft Entra ID tenant and shows the user's name, a minimal-API backend (`MyGarage.Api`) that validates the user's access token and serves a "current user" endpoint plus fixed sample data, a shared DTO library, and an Aspire AppHost that runs both with one command and a dashboard. A PowerShell script (Azure CLI based, idempotent) registers the two Entra app registrations and writes the resulting configuration to AppHost user-secrets. No database is introduced in this feature.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.403 present; pinned via `global.json`)

**Primary Dependencies**: Aspire (AppHost + ServiceDefaults, current 13.x), `Microsoft.Identity.Web` (OIDC for Web, JWT bearer for Api, token acquisition), FluentValidation, ASP.NET Core OpenAPI, Serilog, OpenTelemetry (via ServiceDefaults), Bootstrap 5.3.x (Blazor template asset, pinned)

**Storage**: N/A (no persistence in this feature; `MyGarage.Database` deferred, see Complexity Tracking)

**Testing**: xUnit; `WebApplicationFactory` for API with a locally generated signing key so real JWT validation runs; bUnit for Web components with logic; `Aspire.Hosting.Testing` smoke test for the AppHost. Testcontainers is not needed (no database).

**Target Platform**: Local development on Windows/macOS/Linux with .NET 10 SDK and the Aspire CLI; Azure CLI for the Entra script

**Project Type**: Web application (Blazor UI + minimal API) orchestrated by Aspire

**Performance Goals**: Sign-in to home page < 30 s excluding credential entry; all resources healthy within 60 s of `aspire run` (SC-001, SC-005)

**Constraints**: Web app uses a fixed HTTPS port because Entra web redirect URIs must match exactly; no secrets in source control; token acquisition must work from a static-SSR page (see research R4)

**Scale/Scope**: 2 deployable services + AppHost, ~3 pages, 2 API endpoints

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. .NET 10 / C# only; nullable on; warnings as errors; async + `CancellationToken` | PASS | Enforced in `Directory.Build.props`; endpoints and token-forwarding handler take `CancellationToken`. |
| II. Feature-folder organization | PASS | `Users/`, `SampleData/` in Api; `Authentication/`, `Home/`, `ApiCheck/` in Web; `Common/` for cross-feature. |
| III. Solution structure & dependency direction | PASS (constitution v1.1.2) | `MyGarage.Database` is deferred until a feature needs persistence; `ServiceDefaults` and `AppHost` are permitted by the v1.1.2 amendment. |
| IV. DTO boundary | PASS | Endpoints return `MyGarage.Shared` DTOs only; Web consumes only those; explicit mapping from claims is unit tested. |
| V. Validated minimal APIs | PASS | `MapGroup` per feature, FluentValidation on inputs, `ProblemDetails` for all errors, OpenAPI metadata. `GET /users/me` has no input to validate. |
| VI. Dapper/PostgreSQL/SQL scripts | N/A | No data access in this feature. |
| VII. Versioned migrations | N/A | No schema in this feature. |
| VIII. Test-first, real dependencies | PASS | Tests written first; no database to mock; JWT validation exercised for real. |
| Auth: Entra OIDC (Web) + JWT bearer (Api); every endpoint authorized | PASS | Only Aspire `/health` and `/alive` (development only) are anonymous; they return no user data. |
| Observability: Serilog + OpenTelemetry in Api and Web | PASS | Via ServiceDefaults; no tokens or claims logged. |
| Data protection: identity from validated claims only | PASS | Owner/user identifiers never read from request content (FR-009). |
| Dependencies justified | PASS | See [research.md](research.md) R1-R9. |

**Post-design re-check (after Phase 1)**: unchanged. Constitution amended to v1.1.2 (2026-10-31) to cover `MyGarage.ServiceDefaults`, `MyGarage.AppHost` (referenced only by its test project), and deferral of `MyGarage.Database`; no deviations remain.

## Project Structure

### Documentation (this feature)

```text
specs/001-app-foundation-auth/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── api.openapi.yaml
│   ├── web-auth-routes.md
│   └── entra-setup-script.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
global.json
Directory.Build.props              # net10.0, nullable, TreatWarningsAsErrors, analyzers
Directory.Packages.props           # central package management, pinned versions
MyGarage.slnx

src/
├── MyGarage.AppHost/              # Aspire orchestration; fixed web port; parameters from user-secrets
│   └── AppHost.cs
├── MyGarage.ServiceDefaults/      # OpenTelemetry, health checks, service discovery, resilience, Serilog
│   └── Extensions.cs
├── MyGarage.Api/
│   ├── Program.cs
│   ├── Common/                    # JWT bearer setup, ProblemDetails, claims helpers, OpenAPI
│   ├── Users/                     # GET /api/users/me  (endpoint + claims -> CurrentUserDto mapper)
│   └── SampleData/                # GET /api/sample-data (endpoint + validator + fixed provider)
├── MyGarage.Web/
│   ├── Program.cs
│   ├── Components/                # Blazor app shell (template-standard): App.razor, Routes.razor, Layout/ (MainLayout, NavMenu, LoginDisplay)
│   ├── Common/                    # cross-feature non-UI code: API client, token-forwarding handler, claims helpers
│   ├── Authentication/            # /authentication/login|logout endpoints, RedirectToLogin, signed-out + error pages
│   ├── Home/                      # Home.razor (display name)
│   └── ApiCheck/                  # static-SSR page calling the API with the user's token
└── MyGarage.Shared/
    ├── Users/CurrentUserDto.cs
    └── SampleData/SampleItemDto.cs

infra/
└── entra/
    ├── Initialize-Entra.ps1       # idempotent app registration + user-secrets population
    └── README.md

tests/
├── MyGarage.Api.Tests/
│   ├── Users/
│   ├── SampleData/
│   └── Common/                    # TestJwt factory, WebApplicationFactory fixture
├── MyGarage.Web.Tests/
│   ├── Authentication/            # return-URL validation, redirect behaviour (incl. valid returnUrl round trip), logout
│   ├── Components/Layout/         # LoginDisplay
│   ├── Home/
│   └── ApiCheck/
└── MyGarage.AppHost.Tests/        # Aspire.Hosting.Testing smoke test: resources start, health OK
```

**Structure Decision**: Multi-project solution per constitution III with Aspire AppHost and ServiceDefaults added; source under `src/`, tests under `tests/` mirroring folders, infrastructure scripts under `infra/`. In `MyGarage.Web` the standard Blazor `Components/` folder is kept as the app shell (not a feature and not part of `Common/`); everything feature-specific lives in feature folders.

## Complexity Tracking

No constitution violations. The two items previously logged here (deferring `MyGarage.Database`; `Web`/`Api` referencing `MyGarage.ServiceDefaults`) were resolved by constitution amendment v1.1.2.
