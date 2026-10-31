# Data Model: Application Foundation and User Sign-In

This feature persists nothing. The types below are contract DTOs in `MyGarage.Shared` and configuration shapes.

## DTOs (`MyGarage.Shared`)

### CurrentUserDto (`Users/`)

| Field | Type | Source | Notes |
|---|---|---|---|
| `UserId` | string | `oid` claim (fallback `sub`) | Stable identifier; later features key ownership on it. Required, non-empty. |
| `DisplayName` | string | `name` claim (fallback `preferred_username`, then `"Unknown"`) | Required. |
| `Email` | string? | `preferred_username` / `email` claim | Optional; not logged. |

Derived only from validated token claims; never from request data.

### SampleItemDto (`SampleData/`)

| Field | Type | Notes |
|---|---|---|
| `Id` | int | 1-based, fixed. |
| `Name` | string | Fixed placeholder text, e.g. "Sample item 1". |

`GET /api/sample-data?count=N` returns the first `N` items of a fixed in-memory list (N between 1 and 10, default 3). Not user data.

## Validation rules

- `count`: integer, 1 <= count <= 10 (FluentValidation); otherwise `ProblemDetails` 400.
- `returnUrl` (Web login endpoint): must be a relative path starting with a single `/` (not `//` or `/\`); otherwise replaced with `/`.

## Configuration shape (AppHost user-secrets -> environment)

| Key | Secret | Consumed by |
|---|---|---|
| `AzureAd:TenantId` | no | Web, Api |
| `AzureAd:Web:ClientId` | no | Web |
| `AzureAd:Web:ClientSecret` | yes | Web |
| `AzureAd:Api:ClientId` | no | Web (scope), Api (audience) |
| `AzureAd:Api:Scope` (`access_as_user`) | no | Web, Api |

## State transitions

Session: `Anonymous` -> (login) -> `Authenticated` -> (logout / expiry) -> `Anonymous`. No server-side persisted state beyond the auth cookie and in-memory token cache.
