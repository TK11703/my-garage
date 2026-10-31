# Feature Specification: Application Foundation and User Sign-In

**Feature Branch**: `001-app-foundation-auth`

**Created**: 2026-10-31

**Status**: Draft

**Input**: User description: "Let's build the web application, api, and aspire orchestration as a first item. Along with this we will get the user logged in and authenticated."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in to My Garage (Priority: P1)

A person opens the My Garage web application, is asked to sign in with their organizational account, completes sign-in, and lands on a home page that greets them by name and confirms they are signed in.

**Why this priority**: Every later feature (vehicles, service records) is private to a user. Without a working sign-in there is no identity to attach data to, so this is the minimum viable slice.

**Independent Test**: Start the application, browse to the web address, sign in with a valid account, and confirm the home page shows the signed-in user's display name.

**Acceptance Scenarios**:

1. **Given** a visitor who is not signed in, **When** they open any page of the web application, **Then** they are directed to sign in rather than shown protected content.
2. **Given** a visitor on the sign-in screen, **When** they complete sign-in with a valid account, **Then** they are returned to the page they originally requested (or the home page) and see their display name.
3. **Given** a visitor who cancels or fails sign-in, **When** they return to the application, **Then** they see a clear, friendly message and an option to try again, and no protected content is shown.

---

### User Story 2 - Sign out (Priority: P2)

A signed-in user can sign out from any page. After signing out, protected pages are no longer accessible until they sign in again.

**Why this priority**: Sign-out completes the authentication lifecycle and matters for shared devices, but the application is still demonstrable without it.

**Independent Test**: Sign in, choose sign out, then attempt to open a protected page and confirm sign-in is required again.

**Acceptance Scenarios**:

1. **Given** a signed-in user, **When** they choose sign out, **Then** their session ends and they are shown a signed-out confirmation or the sign-in prompt.
2. **Given** a user who has signed out, **When** they use the browser back button or revisit a protected page, **Then** they are required to sign in again.

---

### User Story 3 - API recognizes the signed-in user (Priority: P2)

When the web application requests data on behalf of the signed-in user, the API verifies who the user is and returns information about that user (identity and display name). Requests without valid proof of sign-in are rejected.

**Why this priority**: The API is the only path to data, and its ability to securely identify the caller is the foundation for per-user data ownership in later features.

**Independent Test**: From the signed-in web application, view the "my profile" information supplied by the API. Separately, call the API without credentials and confirm it is rejected.

**Acceptance Scenarios**:

1. **Given** a signed-in user, **When** the web application asks the API for the current user's information, **Then** the API returns that user's identifier and display name, derived only from the verified sign-in.
2. **Given** a request with no, expired, or invalid proof of sign-in, **When** it reaches the API, **Then** the API refuses it with a standard error response and returns no user data.
3. **Given** a request that includes a user identifier in its content or address that differs from the signed-in user, **When** it reaches the API, **Then** the supplied identifier is ignored and only the verified identity is used.

---

### User Story 4 - Run the whole system locally with one command (Priority: P3)

A developer can use a provided, repeatable setup script (with instructions) that registers the web application and API in the organization's identity tenant, then start the web application and the API together with a single command, see all parts and their health in one place, and have them automatically connected to each other.

**Why this priority**: It speeds up development and verification of every later feature, but end users do not depend on it directly.

**Independent Test**: On a fresh checkout with prerequisites installed, run the setup script and then the single start command, and confirm the web application, API, and a status dashboard are all reachable and report healthy.

**Acceptance Scenarios**:

1. **Given** a fresh checkout and an identity tenant, **When** the developer runs the setup script (more than once, if desired), **Then** the required registrations exist exactly once and the local configuration is populated without secrets being written to source-controlled files.
2. **Given** the setup has been completed, **When** the developer starts the system, **Then** the web application and the API start and the web application can reach the API without manual configuration.
3. **Given** the system is running, **When** the developer opens the status dashboard, **Then** each part is listed with its health, logs, and traces.
4. **Given** a part fails to start, **When** the developer views the dashboard, **Then** the failing part and the reason are visible.

---

### Edge Cases

- A user's session expires while they are using the application: they are prompted to sign in again and, where possible, returned to where they were.
- The identity provider is unavailable: the user sees a friendly "sign-in is temporarily unavailable" message and no protected content.
- A user signs in with an account from outside the allowed organization: access is denied with a clear message.
- The API is unavailable when the web application loads: the user is signed in but sees a friendly error instead of a failed page.
- A request arrives with a token intended for a different audience: the API rejects it.
- A sign-in return address that points to an external site is supplied: the user is sent to the home page instead of the external site.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a web application with a home page that is reachable by a signed-in user.
- **FR-002**: The system MUST require sign-in with an organizational account before any page other than the sign-in flow and public error/status pages is shown.
- **FR-003**: Users MUST be able to sign in, and after sign-in MUST be returned to the page they originally requested, limited to addresses inside the application.
- **FR-004**: The web application MUST display the signed-in user's display name and offer a sign-out action on every page.
- **FR-005**: Users MUST be able to sign out, and after sign-out MUST NOT be able to view protected pages without signing in again.
- **FR-006**: The system MUST provide an API that requires valid proof of sign-in for every endpoint, except health/status endpoints that expose no user data.
- **FR-007**: The API MUST expose an endpoint that returns the current user's stable identifier and display name, derived solely from the verified sign-in.
- **FR-008**: The API MUST reject requests with missing, expired, tampered, or wrong-audience proof of sign-in with a standard problem-details error response.
- **FR-009**: The API MUST ignore any user or owner identifier supplied in request content or addresses for the purpose of identifying or authorizing the caller.
- **FR-010**: The web application MUST call the API on behalf of the signed-in user, and MUST be the only client of the API in this feature.
- **FR-011**: The system MUST show friendly, non-technical messages when sign-in fails, is cancelled, or the identity provider is unavailable, without revealing sensitive details.
- **FR-012**: The system MUST expose health status for the web application and the API.
- **FR-013**: Developers MUST be able to start the web application and the API together with a single command, with the connections between them configured automatically.
- **FR-018**: The repository MUST include a repeatable, idempotent setup script, with written instructions for running it, that registers the web application and the API in the identity tenant (including the permission the web application needs to call the API), and stores the resulting local configuration outside source control.
- **FR-019**: The API MUST expose an endpoint returning fixed sample data so the web application can demonstrate that the API is reachable with the user's sign-in; no persistent storage is involved in this feature.
- **FR-014**: The local orchestration MUST provide a dashboard showing each part's health, logs, and traces.
- **FR-015**: The system MUST NOT store secrets in source control, and MUST NOT log credentials, tokens, or other sensitive user data.
- **FR-016**: The system MUST run from a fresh checkout without any pre-existing data or database.
- **FR-017**: The solution MUST include automated tests covering sign-in-required behavior, API acceptance of valid sign-in, and API rejection of missing or invalid sign-in.

### Key Entities *(include if feature involves data)*

- **Signed-in User**: The person using the application. Attributes available from the verified sign-in: stable unique identifier, display name, and optionally email address. Not persisted by this feature; later features will attach vehicles to this identifier.
- **Current User Information**: The read-only view of the Signed-in User returned by the API (identifier and display name).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user with a valid account can go from opening the application to seeing their name on the home page in under 30 seconds, excluding time spent entering credentials.
- **SC-002**: 100% of attempts to open protected pages or API endpoints without valid sign-in are refused and show no user data.
- **SC-003**: After signing out, 100% of protected pages require sign-in again.
- **SC-004**: A developer on a fresh checkout can have the complete system running and healthy with one command in under 5 minutes, excluding first-time downloads.
- **SC-005**: All parts of the running system report healthy on the status dashboard within 60 seconds of startup.
- **SC-006**: 100% of sign-in failure scenarios listed above show a friendly message and never an unhandled error page.

## Assumptions

- Sign-in uses the organization's existing Microsoft Entra ID tenant; self-registration, passwords managed by My Garage, and social sign-in are out of scope.
- Only accounts from the configured tenant are allowed; guest and external accounts are denied.
- Roles, permissions beyond "signed in", and multi-factor policy are managed by the identity provider and are out of scope.
- No user record or other data is persisted by this feature; the data store, user profile, vehicles, and service records are delivered by later specs. The API serves fixed sample data only.
- The tenant app registrations (one for the web application, one for the API) are created by running the provided setup script, which is executed outside this feature's automated build and tests; this feature delivers the script and written instructions only. Tenant access and administrator consent are supplied by the person running it. Non-secret identifiers are supplied through configuration, and secrets are kept outside source control.
- The only client of the API in this feature is the web application; mobile and third-party clients are out of scope.
- Production hosting and deployment (infrastructure as code) are out of scope; this feature targets local development and automated tests.
- Standard web session behavior applies: sessions end on sign-out or when the identity provider session policy expires them.
