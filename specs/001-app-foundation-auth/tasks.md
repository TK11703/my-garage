---

description: "Task list for Application Foundation and User Sign-In"
---

# Tasks: Application Foundation and User Sign-In

**Input**: Design documents from `/specs/001-app-foundation-auth/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included. The constitution (VIII) requires test-first development: write each story's tests, run them, and confirm they FAIL before implementing.

**Organization**: Tasks are grouped by user story. Paths follow the structure in plan.md.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 sign in, US2 sign out, US3 API recognizes user, US4 one-command local run + Entra setup

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Solution skeleton that builds with zero warnings.

- [X] T001 Create `global.json` (pin .NET SDK 10.0.x), `Directory.Build.props` (net10.0, C# 14, `Nullable` enabled, `TreatWarningsAsErrors` true, `ImplicitUsings`), and `Directory.Packages.props` (central package management; pin Aspire 13.x, Microsoft.Identity.Web, FluentValidation, Serilog, OpenTelemetry, xUnit, bUnit, Aspire.Hosting.Testing, Microsoft.AspNetCore.Mvc.Testing) at the repo root
- [X] T002 Create `MyGarage.slnx` at the repo root and the class library `src/MyGarage.ServiceDefaults/MyGarage.ServiceDefaults.csproj`
- [X] T003 [P] Create class library `src/MyGarage.Shared/MyGarage.Shared.csproj` (no project references)
- [X] T004 [P] Create minimal API project `src/MyGarage.Api/MyGarage.Api.csproj` (references Shared and ServiceDefaults) with a bare `Program.cs`
- [X] T005 [P] Create Blazor Web App `src/MyGarage.Web/MyGarage.Web.csproj` (Interactive Server render mode, references Shared and ServiceDefaults); keep Bootstrap pinned to a 5.3.x version under `wwwroot/lib/bootstrap`; remove template sample pages (Counter, Weather)
- [X] T006 [P] Create xUnit project `tests/MyGarage.Api.Tests/MyGarage.Api.Tests.csproj` (references Api, Shared; Mvc.Testing)
- [X] T007 [P] Create xUnit project `tests/MyGarage.Web.Tests/MyGarage.Web.Tests.csproj` (references Web, Shared; bUnit, Mvc.Testing)
- [X] T008 [P] Create the Aspire AppHost project `src/MyGarage.AppHost/MyGarage.AppHost.csproj` (Aspire AppHost SDK; project references to Api and Web) and xUnit project `tests/MyGarage.AppHost.Tests/MyGarage.AppHost.Tests.csproj` (Aspire.Hosting.Testing; references the AppHost, which constitution III permits only for this test project)
- [X] T009 Add all projects to `MyGarage.slnx` (solution folders `src`, `tests`) and verify `dotnet build MyGarage.slnx` succeeds with zero warnings

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-cutting pieces every story depends on.

**CRITICAL**: No user story work begins until this phase is complete.

- [X] T010 Implement `AddServiceDefaults`/`MapDefaultEndpoints` in `src/MyGarage.ServiceDefaults/Extensions.cs`: OpenTelemetry logs/metrics/traces with OTLP export, service discovery, standard HTTP resilience, health checks; map `/health` and `/alive` in Development only (FR-012, FR-014)
- [X] T011 Add Serilog setup helper in `src/MyGarage.ServiceDefaults/SerilogExtensions.cs` (console sink, OTel enrichment; request logging that excludes headers, tokens, and claim values) (FR-015)
- [X] T012 [P] Create fail-fast settings class `src/MyGarage.Api/Common/EntraApiSettings.cs` binding `AzureAd:TenantId`, `AzureAd:Api:ClientId`, `AzureAd:Api:Scope` (see data-model.md Configuration shape); startup fails with a clear message if missing
- [X] T013 [P] Create fail-fast settings class `src/MyGarage.Web/Common/EntraWebSettings.cs` binding `AzureAd:TenantId`, `AzureAd:Web:ClientId`, `AzureAd:Web:ClientSecret`, `AzureAd:Api:ClientId`, `AzureAd:Api:Scope`
- [X] T014 [P] Configure RFC 9457 `ProblemDetails`, global exception handler, and status-code pages (401/403/404 as `application/problem+json`) in `src/MyGarage.Api/Common/ProblemDetailsSetup.cs`

**Checkpoint**: Foundation ready; story phases can begin.

---

## Phase 3: User Story 1 - Sign in to My Garage (Priority: P1) MVP

**Goal**: An unauthenticated visitor is sent to Entra sign-in and returns to a home page showing their display name; failures show friendly messages.

**Independent Test**: Run `MyGarage.Web.Tests`; with live Entra (after US4), browse to `https://localhost:7080`, sign in, and see your name.

### Tests for User Story 1 (write first, confirm they fail)

- [X] T015 [P] [US1] `ReturnUrlValidatorTests` in `tests/MyGarage.Web.Tests/Authentication/ReturnUrlValidatorTests.cs`: accepts `/`, `/api-check`; rejects null, empty, `//evil.com`, `/\evil.com`, `https://evil.com`, `javascript:` and returns `/`
- [X] T016 [P] [US1] `AuthFailureMapperTests` in `tests/MyGarage.Web.Tests/Authentication/AuthFailureMapperTests.cs`: user-cancelled -> `cancelled`, access denied/wrong tenant -> `denied`, remote/network failure -> `unavailable`, anything else -> `unknown`
- [X] T017 [P] [US1] `LoginRedirectTests` in `tests/MyGarage.Web.Tests/Authentication/LoginRedirectTests.cs` (WebApplicationFactory with dummy Entra config and a static `OpenIdConnectConfiguration` so no network call to Entra is made): anonymous `GET /protected-path` returns a challenge whose return target is that path; `GET /authentication/login?returnUrl=/api-check` round-trips to `/api-check`; `GET /authentication/login?returnUrl=https://evil.com` issues a challenge whose return target is `/`
- [X] T018 [P] [US1] `HomeTests` in `tests/MyGarage.Web.Tests/Home/HomeTests.cs` (bUnit with fake auth): shows the `name` claim; falls back to `preferred_username`; then a neutral label
- [X] T019 [P] [US1] `AuthenticationErrorPageTests` in `tests/MyGarage.Web.Tests/Authentication/AuthenticationErrorPageTests.cs` (bUnit): fixed friendly message per reason, unknown reason shows the `unknown` message, raw `reason` text is never rendered, "Try again" links to `/authentication/login`

### Implementation for User Story 1

- [X] T020 [P] [US1] Implement `ReturnUrlValidator` in `src/MyGarage.Web/Authentication/ReturnUrlValidator.cs` (local relative path starting with a single `/`; otherwise `/`) (FR-003)
- [X] T021 [P] [US1] Implement `AuthFailureMapper` in `src/MyGarage.Web/Authentication/AuthFailureMapper.cs` (reason codes from contracts/web-auth-routes.md)
- [X] T022 [US1] Configure Entra OIDC in `src/MyGarage.Web/Authentication/AuthenticationExtensions.cs`: `AddMicrosoftIdentityWebApp` (single-tenant authority, code flow, client secret), `EnableTokenAcquisitionToCallDownstreamApi` with in-memory token cache, fallback authorization policy requiring an authenticated user, `OnRemoteFailure`/`OnAccessDenied` redirecting to `/authentication/error?reason=` via `AuthFailureMapper`, cookie expiry triggers a new challenge (FR-002, FR-011)
- [X] T023 [US1] Add `GET /authentication/login` in `src/MyGarage.Web/Authentication/AuthenticationEndpoints.cs` (`MapGroup("/authentication")`, anonymous, validates `returnUrl` with `ReturnUrlValidator`, issues OIDC challenge)
- [X] T024 [P] [US1] Create `src/MyGarage.Web/Authentication/AuthenticationError.razor` (`/authentication/error`, anonymous; Bootstrap alert, accessible, no raw provider text)
- [X] T025 [P] [US1] Create `src/MyGarage.Web/Home/Home.razor` (`/`) greeting the user by display name, with name resolution in `src/MyGarage.Web/Common/ClaimsPrincipalExtensions.cs` (`name` -> `preferred_username` -> neutral label)
- [X] T026 [US1] Wire `src/MyGarage.Web/Program.cs` and the standard shell `src/MyGarage.Web/Components/App.razor` and `src/MyGarage.Web/Components/Routes.razor`: Serilog + ServiceDefaults, `AddCascadingAuthenticationState`, `AuthorizeRouteView` rendering `src/MyGarage.Web/Authentication/RedirectToLogin.razor` for unauthenticated users (it forwards the current relative URI as `returnUrl`, so sign-in and session expiry return the user to the requested page), Interactive Server render mode, map authentication endpoints, Development-only health endpoints

**Checkpoint**: US1 tests pass; sign-in flow works against a configured tenant.

---

## Phase 4: User Story 2 - Sign out (Priority: P2)

**Goal**: A signed-in user can sign out from any page and loses access to protected pages.

**Independent Test**: Run the US2 tests; sign in, sign out, then confirm `/` requires sign-in again.

### Tests for User Story 2 (write first, confirm they fail)

- [X] T027 [P] [US2] `LogoutEndpointTests` in `tests/MyGarage.Web.Tests/Authentication/LogoutEndpointTests.cs`: `GET /authentication/logout` is not allowed (405/404); anonymous POST rejected; authenticated POST with antiforgery clears the cookie and redirects to the identity provider sign-out, returning to `/authentication/signed-out`
- [X] T028 [P] [US2] `SignedOutPageTests` in `tests/MyGarage.Web.Tests/Authentication/SignedOutPageTests.cs` (bUnit): confirmation text and a "Sign in" link
- [X] T029 [P] [US2] `LoginDisplayTests` in `tests/MyGarage.Web.Tests/Components/Layout/LoginDisplayTests.cs` (bUnit): authenticated -> display name and sign-out form with antiforgery token; anonymous -> "Sign in" link

### Implementation for User Story 2

- [X] T030 [US2] Add `POST /authentication/logout` (antiforgery required, authorized) in `src/MyGarage.Web/Authentication/AuthenticationEndpoints.cs`: sign out cookie and OIDC scheme, post-logout redirect to `/authentication/signed-out` (FR-005)
- [X] T031 [P] [US2] Create `src/MyGarage.Web/Authentication/SignedOut.razor` (`/authentication/signed-out`, anonymous)
- [X] T032 [US2] Create `src/MyGarage.Web/Components/Layout/LoginDisplay.razor` (display name + sign-out form with `<AntiforgeryToken />`; keyboard accessible) and include it in the standard `src/MyGarage.Web/Components/Layout/MainLayout.razor` so it appears on every page (FR-004)

**Checkpoint**: US1 and US2 work independently.

---

## Phase 5: User Story 3 - API recognizes the signed-in user (Priority: P2)

**Goal**: The API validates the caller's access token, returns the caller's identity and fixed sample data, rejects everything else with `ProblemDetails`; the web app calls it with the user's token.

**Independent Test**: Run `MyGarage.Api.Tests` and the Web `ApiCheck` tests; with live Entra open `/api-check`.

### Tests for User Story 3 (write first, confirm they fail)

- [X] T033 [US3] Create test helpers `tests/MyGarage.Api.Tests/Common/TestJwt.cs` (mints tokens with a locally generated signing key; options for issuer, audience, expiry, scope, claims) and `tests/MyGarage.Api.Tests/Common/ApiFactory.cs` (`WebApplicationFactory<Program>` that overrides JWT bearer validation parameters to trust the test key and supplies dummy Entra settings)
- [X] T034 [P] [US3] `CurrentUserEndpointTests` in `tests/MyGarage.Api.Tests/Users/CurrentUserEndpointTests.cs`: valid token -> 200 `CurrentUserDto` from claims; no token/expired/wrong audience/wrong issuer/bad signature -> 401 `application/problem+json`; missing `access_as_user` scope -> 403 `ProblemDetails`; `?userId=` / `X-User-Id` / body-supplied identifiers are ignored (FR-006 to FR-009)
- [X] T035 [P] [US3] `CurrentUserMapperTests` in `tests/MyGarage.Api.Tests/Users/CurrentUserMapperTests.cs`: `oid` preferred over `sub`; `name` -> `preferred_username` -> `"Unknown"`; email optional
- [X] T036 [P] [US3] `SampleDataEndpointTests` in `tests/MyGarage.Api.Tests/SampleData/SampleDataEndpointTests.cs`: default 3 items; `count=5` -> 5 items; `count=0`, `11`, `abc` -> 400 `ValidationProblemDetails`; no token -> 401; unknown route -> 404 `ProblemDetails`
- [X] T037 [P] [US3] `SampleDataRequestValidatorTests` in `tests/MyGarage.Api.Tests/SampleData/SampleDataRequestValidatorTests.cs`: `count` valid only when `1 <= count <= 10`
- [X] T038 [P] [US3] `ApiCheckPageTests` in `tests/MyGarage.Web.Tests/ApiCheck/ApiCheckPageTests.cs` (fake `HttpMessageHandler`): renders the user's identity and sample items on success; shows a friendly message when the API is unavailable or returns 401/403
- [X] T039 [P] [US3] `ApiAccessTokenHandlerTests` in `tests/MyGarage.Web.Tests/Common/ApiAccessTokenHandlerTests.cs`: attaches a bearer token for scope `api://<AzureAd:Api:ClientId>/access_as_user`; surfaces token-acquisition failure without logging the token

### Implementation for User Story 3

- [X] T040 [P] [US3] Create `CurrentUserDto` (`UserId` required non-empty string, `DisplayName` required string, `Email` nullable string) in `src/MyGarage.Shared/Users/CurrentUserDto.cs`
- [X] T041 [P] [US3] Create `SampleItemDto` (`Id` int, `Name` string) in `src/MyGarage.Shared/SampleData/SampleItemDto.cs`
- [X] T042 [US3] Configure JWT bearer in `src/MyGarage.Api/Common/AuthenticationExtensions.cs`: `AddMicrosoftIdentityWebApi`, v2 issuer for the tenant, audience `api://<clientId>` and clientId, lifetime validation, `access_as_user` scope policy, fallback policy requiring authenticated user with that scope (FR-006, FR-008)
- [X] T043 [P] [US3] Implement `CurrentUserMapper` in `src/MyGarage.Api/Users/CurrentUserMapper.cs` (claims -> `CurrentUserDto`; never reads request data)
- [X] T044 [US3] Implement `GET /api/users/me` in `src/MyGarage.Api/Users/UsersEndpoints.cs` (`MapGroup("/api/users")`, declares 200/401/403, OpenAPI metadata)
- [X] T045 [P] [US3] Implement `SampleDataRequest` (`Count`, default 3), `SampleDataRequestValidator` (FluentValidation, `InclusiveBetween(1, 10)`) and fixed in-memory `SampleDataProvider` in `src/MyGarage.Api/SampleData/`
- [X] T046 [P] [US3] Implement a FluentValidation endpoint filter returning `ValidationProblem` (HTTP 400, RFC 9457) in `src/MyGarage.Api/Common/ValidationFilter.cs`
- [X] T047 [US3] Implement `GET /api/sample-data` in `src/MyGarage.Api/SampleData/SampleDataEndpoints.cs` (`MapGroup("/api")`, validation filter, declares 200/400/401/403, OpenAPI metadata)
- [X] T048 [US3] Wire `src/MyGarage.Api/Program.cs`: Serilog + ServiceDefaults, authentication/authorization, ProblemDetails, OpenAPI (Development), feature endpoint groups, `public partial class Program` for tests; Development-only anonymous health endpoints
- [X] T049 [US3] Implement `ApiAccessTokenHandler` (`DelegatingHandler` using `ITokenAcquisition.GetAccessTokenForUserAsync`, propagates `CancellationToken`) in `src/MyGarage.Web/Common/ApiAccessTokenHandler.cs` and a typed `GarageApiClient` returning Shared DTOs in `src/MyGarage.Web/Common/GarageApiClient.cs`
- [X] T050 [US3] Register the typed client in `src/MyGarage.Web/Program.cs` with service-discovery address `https+http://api` and `ApiAccessTokenHandler`
- [X] T051 [US3] Create `src/MyGarage.Web/ApiCheck/ApiCheck.razor` (`/api-check`, static SSR via `[ExcludeFromInteractiveRouting]`, calls `GarageApiClient`, friendly error on failure) and add a nav link in `src/MyGarage.Web/Components/Layout/NavMenu.razor`

**Checkpoint**: US1 to US3 work; API protected and reachable from the web app with the user's token.

---

## Phase 6: User Story 4 - Entra setup and one-command local run (Priority: P3)

**Goal**: A developer runs one idempotent script to register the apps, then starts everything with `aspire run` and sees both services healthy on the dashboard.

**Independent Test**: Run `MyGarage.AppHost.Tests`; follow quickstart.md steps 1 to 2.

### Tests for User Story 4 (write first, confirm they fail)

- [X] T052 [US4] `AppHostSmokeTests` in `tests/MyGarage.AppHost.Tests/AppHostSmokeTests.cs` (Aspire.Hosting.Testing with dummy Entra parameters): `api` and `web` reach Running/healthy within 60 s; web resource has the fixed HTTPS endpoint on port 7080 and a reference to `api`

### Implementation for User Story 4

- [X] T053 [US4] Implement `src/MyGarage.AppHost/AppHost.cs`: parameters `AzureAd:TenantId`, `AzureAd:Web:ClientId`, `AzureAd:Web:ClientSecret` (secret), `AzureAd:Api:ClientId`, `AzureAd:Api:Scope`; `api` project; `web` project with `WithReference(api)`, `WaitFor(api)`, fixed HTTPS port 7080, external endpoints, and environment variables mapping the parameters (FR-013, FR-014)
- [X] T054 [P] [US4] Add `UserSecretsId` to the AppHost project and a non-secret `src/MyGarage.AppHost/appsettings.json` (no tenant or secret values); ensure `launchSettings.json` runs the dashboard with HTTPS
- [X] T055 [P] [US4] Add `src/MyGarage.Web/Properties/launchSettings.json` using `https://localhost:7080` so standalone runs match the registered redirect URI
- [X] T056 [US4] Implement `infra/entra/Initialize-Entra.ps1` per contracts/entra-setup-script.md: parameters `-TenantId`, `-WebPort`, `-NamePrefix`, `-AppHostProject`, `-RotateSecret`, `-WhatIf`; precondition checks; find-or-create API app (identifier URI, `access_as_user`, token v2) and Web app (redirect + logout URIs, delegated permission, consent attempt); create secret only when none stored; write values to AppHost user-secrets; never print the secret; non-zero exit on failure (FR-018). The live run against the tenant is executed outside this feature; `-WhatIf` must work without changing anything
- [X] T057 [P] [US4] Write `infra/entra/README.md` (prerequisites, step-by-step instructions for running the script against a tenant, required tenant permissions and admin consent, idempotence, rotating the secret, troubleshooting) and update `infra/README.md` to link it (FR-018)

**Checkpoint**: All four stories work; the script supports a `-WhatIf` dry run and its instructions are complete. Live execution against the tenant and the quickstart sign-in checks are performed outside this feature.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T058 [P] Update root `README.md` with prerequisites (.NET 10 SDK, Aspire CLI, Azure CLI, dev certificate) and a pointer to `specs/001-app-foundation-auth/quickstart.md`
- [X] T059 [P] Review logging and telemetry in `src/MyGarage.Api` and `src/MyGarage.Web`: confirm no tokens, secrets, or claim values are logged (FR-015); confirm no client secrets or tenant-specific values are in tracked files (the tenant ID is supplied to the setup script with `-TenantId`, never hard-coded)
- [X] T060 Verify `dotnet build MyGarage.slnx` has zero warnings and `dotnet test MyGarage.slnx` passes
- [X] T061 Review `quickstart.md` and `infra/entra/README.md` for accuracy against the implemented script and AppHost, and run `Initialize-Entra.ps1 -WhatIf` as a dry run; the live tenant run and manual sign-in checks (quickstart steps 1 to 5) are executed outside this feature

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none; start immediately.
- **Foundational (Phase 2)**: depends on Setup; blocks all stories.
- **US1 (P1)**: depends on Foundational.
- **US2 (P2)**: depends on Foundational; shares `AuthenticationEndpoints.cs`, `MainLayout.razor`, so do after US1 (T030 follows T023).
- **US3 (P2)**: API half (T033 to T048) depends only on Foundational and can run in parallel with US1/US2; the Web half (T049 to T051) depends on US1 (T022, T026).
- **US4 (P3)**: depends on Foundational; the script (T056) is independent, the AppHost (T053) needs the Api and Web projects to exist (Phase 1) and is most useful after US1 and US3.
- **Polish**: after all desired stories.

### Within Each Story

- Tests first and failing, then helpers/DTOs, then services, then endpoints/pages, then wiring.

### Story Dependency Graph

```text
Setup -> Foundational -> US1 -> US2
                    \-> US3 (API part) ----\
                    US1 -> US3 (Web part) ---> Polish
                    Foundational -> US4 -----/
```

## Parallel Opportunities

- Setup: T003 to T008 together after T002.
- Foundational: T012, T013, T014 together after T010/T011.
- US1 tests T015 to T019 together; implementation T020, T021, T024, T025 together.
- US2 tests T027 to T029 together; T031 parallel to T030.
- US3 tests T034 to T039 together; T040/T041/T043/T045/T046 together; the API half and US1/US2 can be staffed in parallel.
- US4: T054, T055, T057 in parallel; T056 can start any time after Setup.

### Parallel Example: User Story 3 (API half)

```text
T034 CurrentUserEndpointTests   T035 CurrentUserMapperTests
T036 SampleDataEndpointTests    T037 SampleDataRequestValidatorTests
then:  T040 CurrentUserDto  T041 SampleItemDto  T043 CurrentUserMapper  T045 SampleData validator/provider  T046 ValidationFilter
```

## Implementation Strategy

### MVP First

1. Phase 1 and 2.
2. US1 (sign in) and validate with tests.
3. For a live sign-in demo, also do T056 (Entra script) and T053 to T055 (AppHost, port 7080) from US4; the MVP is not demonstrable against a real tenant without them.

### Incremental Delivery

1. Foundation + US1 (+ US4 script/AppHost) -> demo sign-in showing the user's name.
2. US2 -> sign-out.
3. US3 -> protected API and `/api-check` proof of reachability.
4. Polish.

## Notes

- Each task names an exact file; commit after each task or logical group using Conventional Commits.
- Do not commit secrets; the web client secret lives only in AppHost user-secrets.
- Confirm tests fail before implementing (constitution VIII).
