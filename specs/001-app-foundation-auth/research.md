# Research: Application Foundation and User Sign-In

No `NEEDS CLARIFICATION` items remained in the Technical Context; the entries below record the design decisions.

## R1. Orchestration with Aspire

- **Decision**: `MyGarage.AppHost` (Aspire AppHost SDK, .NET 10) adds `api` and `web` project resources; `web` references `api` for service discovery and waits for it. Run with `aspire run` (or F5 on the AppHost).
- **Rationale**: Gives one-command start, dashboard (health/logs/traces), and service discovery without hard-coded URLs (FR-013, FR-014).
- **Alternatives**: docker-compose or manual `dotnet run` (no dashboard, manual wiring).

## R2. Entra app registrations and scripting

- **Decision**: One idempotent PowerShell script using Azure CLI (`az ad app`, `az rest` to Microsoft Graph). It creates:
  - **API app** `MyGarage API`: single tenant, Application ID URI `api://<api-client-id>`, delegated scope `access_as_user`, `requestedAccessTokenVersion = 2`.
  - **Web app** `MyGarage Web`: single tenant, Web platform redirect URI `https://localhost:<port>/signin-oidc`, front-channel logout / post-logout URI `https://localhost:<port>/signout-callback-oidc`, ID tokens off (code flow), delegated permission to the API scope with admin consent attempted when permitted, and a client secret.
  - Looks up apps by `displayName`/`uniqueName`-style tag so reruns update rather than duplicate.
  - Writes `AzureAd:TenantId`, `AzureAd:Web:ClientId`, `AzureAd:Web:ClientSecret`, `AzureAd:Api:ClientId` to the AppHost's user-secrets; never prints or writes the secret to a tracked file.
- **Rationale**: Azure CLI is already installed on the dev machine; no extra PowerShell module needed. Idempotence is required by FR-018.
- **Alternatives**: Microsoft Graph PowerShell SDK (extra module install); Bicep Microsoft Graph extension (preview, tenant-level permissions); portal clicks (not repeatable).

## R3. Sign-in flow for the Blazor Web App

- **Decision**: `Microsoft.Identity.Web` `AddMicrosoftIdentityWebApp` (cookie + OIDC, authorization code flow with client secret) with `EnableTokenAcquisitionToCallDownstreamApi` and in-memory token cache. Authority is the single tenant. A global fallback authorization policy requires an authenticated user. Our own minimal endpoints `/authentication/login?returnUrl=` and `/authentication/logout` trigger challenge/sign-out; `returnUrl` is accepted only if it is a local relative path, otherwise `/` is used (spec edge case). `OnRemoteFailure` and denied-account results redirect to `/authentication/error?reason=` with friendly text (FR-011).
- **Rationale**: Constitution mandates Entra OIDC. Own endpoints keep return-URL validation testable and avoid the stock UI controllers.
- **Alternatives**: Stock `Microsoft.Identity.Web.UI` account controller (less control of open-redirect handling); BFF/YARP (out of scope).

## R4. Calling the API with the user's token from Blazor Server

- **Decision**: A `DelegatingHandler` obtains the token via `ITokenAcquisition.GetAccessTokenForUserAsync(["api://<api-client-id>/access_as_user"])` and attaches it to the typed API client. The API-check page opts out of interactive routing (static SSR) so an `HttpContext` is available; the Home page remains Interactive Server.
- **Rationale**: `HttpContext` is not reliably available inside interactive circuits, which makes token acquisition fragile there. Static SSR sidesteps it for this feature. Revisit (circuit-safe token provider) when interactive pages need API calls.
- **Alternatives**: Make the whole app static SSR (violates constitution's render mode); copy tokens into circuit state (security and lifetime complexity).

## R5. API authentication

- **Decision**: `AddMicrosoftIdentityWebApi` (JWT bearer) validating issuer (tenant, v2), audience (`api://<api-client-id>` and client ID), lifetime, and requiring the `access_as_user` scope via an authorization policy. `FallbackPolicy` requires an authenticated user with that scope; the user id is the `oid` claim (fallback `sub`), name from `name`/`preferred_username`.
- **Rationale**: Constitution (JWT bearer; identity only from claims). Fallback policy makes "authorize by default" structural.
- **Alternatives**: Hand-rolled `AddJwtBearer` (more code, same result).

## R6. Endpoints, validation, errors, OpenAPI

- **Decision**: `GET /api/users/me` and `GET /api/sample-data?count=` under per-feature `MapGroup`s. `count` (1-10, default 3) is the only input and is validated with FluentValidation via an endpoint filter that yields `ProblemDetails` (RFC 9457, HTTP 400 `ValidationProblem`). `AddProblemDetails` + exception handler + status-code pages for 401/403/404. OpenAPI document via `Microsoft.AspNetCore.OpenApi`, served in Development.
- **Rationale**: Constitution V; gives the sample endpoint a real validation path to test.
- **Alternatives**: Endpoint with no input (nothing for the validation principle to exercise).

## R7. Observability

- **Decision**: ServiceDefaults configures OpenTelemetry (logs, metrics, traces, OTLP export to the Aspire dashboard), health checks (`/health`, `/alive` mapped in Development only), service discovery, and standard resilience. Serilog is configured in each host with the console sink and OTel enrichment; request logging excludes headers and tokens.
- **Rationale**: Constitution requires both; FR-012/FR-014/FR-015.
- **Alternatives**: Per-project duplication (see Complexity Tracking).

## R8. Fixed ports and HTTPS

- **Decision**: AppHost pins the web project to a fixed HTTPS port (default `7080`, shared with the script parameter `-WebPort`); the API port stays dynamic (only reached via service discovery). Dev HTTPS certificate via `dotnet dev-certs https --trust`.
- **Rationale**: Entra web redirect URIs need an exact match including port.
- **Alternatives**: Dynamic ports plus per-run redirect registration (brittle).

## R9. Test strategy

- **Decision**:
  - Api: `WebApplicationFactory<Program>` configured with a test signing key and matching issuer/audience; a `TestJwt` helper mints tokens. Cases: valid token -> 200 with claim-derived identity; none/expired/wrong-audience/wrong-signature/missing-scope -> 401/403 `ProblemDetails`; spoofed `userId` in query/body ignored; `count` out of range -> 400 `ProblemDetails`; unknown route -> 404 `ProblemDetails`.
  - Web: unauthenticated request to `/` returns a challenge redirect; return-URL validation (local only); claim-to-display-name mapping; bUnit for the header/greeting component; error page messages.
  - AppHost: `Aspire.Hosting.Testing` starts api and web with dummy configuration and asserts `/health` is healthy.
  - Tests are written first and must fail before implementation.
- **Rationale**: Covers FR-017 without calling the real tenant.
- **Alternatives**: Live-tenant end-to-end tests (secrets in CI, flaky); documented as a manual check in the quickstart.
