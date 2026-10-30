# Copilot Instructions

The project constitution at `.specify/memory/constitution.md` is authoritative and takes precedence over these instructions.

## Architecture

Preserve existing architecture, project boundaries, and dependency direction. Follow established patterns before introducing new abstractions, dependencies, frameworks, or architectural approaches. Align new features with adjacent implementations unless a documented reason exists to diverge. Keep API, HTTP, authentication, authorization, and orchestration concerns in the application layer; persistence, migrations, and data access in the data layer; presentation and UI behavior in the UI layer.

## Solution Structure

Organize source projects under `src/`, test projects under `tests/`, and infrastructure assets under `infra/`. Prefer feature-based organization over technical-layer organization. Keep related endpoints, DTOs, validators, services, repositories, components, tests, and supporting assets together. Test projects should mirror the structure of the project under test.

## Security & Authorization

Derive identity, ownership, and permissions from validated authentication claims. Never trust client-provided ownership, identity, or authorization data. Enforce authorization server-side and apply least-privilege principles. Validate all untrusted input at system boundaries before database or external-service operations. Never commit secrets, credentials, keys, or connection strings, and never log secrets or sensitive data.

## API & Application Design

Keep endpoints, controllers, handlers, and UI entry points thin. Place business logic in focused services and persistence logic in repositories or data-access components. Use dependency injection and avoid static mutable state. Never expose persistence entities as external contracts; use DTOs or public models and map explicitly between layers. Define clear request and response contracts, validate requests, and return standardized error responses. Preserve nullable-reference-type safety and prefer asynchronous APIs with `CancellationToken` propagation throughout applicable layers.

## Data Access

Use parameterized queries exclusively. Never concatenate or interpolate values into SQL. Store SQL in dedicated script files where applicable rather than embedding queries in application code. Keep data-access operations asynchronous and accept `CancellationToken`. Apply schema changes through numbered, forward-only migration scripts; never modify an applied migration.

## Date & Time

Store absolute instants in UTC using `DateTimeOffset`. Use date-only types for date-only values. When local user times are required, persist sufficient information to reconstruct the original local time and time zone accurately.

## UI Standards

Prefer approved platform components and built-in framework capabilities over custom solutions. Use semantic HTML, accessible design patterns, keyboard navigation, appropriate focus management, and accessible names for icon-only controls. Keep JavaScript modular and narrowly scoped. Dispose of event handlers and subscriptions correctly. Avoid manipulating framework-owned DOM unless explicitly required. Safely render user-generated content and respect accessibility preferences such as reduced motion.

Provide meaningful `alt` text for informative images and empty `alt=""` attributes for decorative images, and use associated labels or appropriate ARIA attributes to give controls accessible names. Use `title` attributes only for supplemental information, never as the sole source of a label, instruction, or essential content.

## Testing

Follow a test-first approach when practical. Ensure features include automated tests covering success, validation, authorization, error, and business-rule scenarios. Keep tests deterministic, isolated, and repeatable. Avoid shared mutable state and use real infrastructure dependencies when required by project standards.

## Workflow

Use specification-driven development with the repository's approved workflow. Work from documented requirements, specifications, or user stories. Keep changes focused and traceable to a single work item. Do not commit directly to protected branches; use pull requests for review and integration. Before completing work, ensure the solution builds cleanly, static analysis passes, tests pass, and migrations apply successfully where applicable.

## Documentation

Keep README, onboarding material, architecture documentation, and specifications current. Document significant design decisions, deviations from established patterns, and changes affecting future development.

## Quality Gates

Do not introduce new packages, frameworks, ORMs, CSS frameworks, or major architectural patterns without documented justification. Prefer consistency with existing project conventions over personal preference.

## Commits

Use Conventional Commits: `feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`, and `chore`. Use concise imperative subjects without trailing punctuation.