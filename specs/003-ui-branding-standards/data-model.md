# Data Model: UI Branding and Standardization

This feature stores and changes no data: no tables, migrations, DTOs, or API contracts. The only "model" is a set of in-memory presentation types in `MyGarage.Web` and the brand/UI-standard assets. All of them are static or derived from the signed-in user's own claims.

## In-memory presentation types

### Crumb

One step of a breadcrumb trail.

| Field | Type | Rules |
|---|---|---|
| `Label` | string | Required, non-empty, rendered as encoded text (vehicle nicknames are user text). |
| `Href` | string? | Optional app-relative path. Ignored for the last crumb, which is always the current page and never a link. |

The implicit first crumb "Home" (`/`) is added by `PageHeader`; pages never supply it.

### PageHeader parameters

| Parameter | Type | Rules |
|---|---|---|
| `Title` | string | Required. Becomes the single `h1` and the browser tab title `"{Title} – My Garage"`. |
| `Trail` | `IReadOnlyList<Crumb>` | Zero or more crumbs after "Home". Empty on the home page (the trail is just "Home"). |
| `Actions` | `RenderFragment?` | Optional primary action(s) rendered right of the title. |

### Navigation bar state (derived, not stored)

| Value | Source | Rules |
|---|---|---|
| `IsSignedIn` | Cascading authentication state | Selects signed-in vs signed-out bar parts. |
| `DisplayName` | `ClaimsPrincipalExtensions.GetDisplayName()` (existing; `name` then `preferred_username` claims) | The signed-in user's own name only; neutral fallback "Signed-in user". |
| `Initials` | `ClaimsPrincipalExtensions.GetInitials()` (new) | Up to two uppercase letters from the first letters of the first and last name words; empty for the neutral name (a person icon is shown instead). |
| `SearchText` | `q` form field | Max 100 characters (same as the garage search); sent as GET to `/vehicles`. |

## Brand and UI-standard assets (not data)

| Asset | Location | Notes |
|---|---|---|
| Logo mark (dark background) | `src/MyGarage.Web/wwwroot/images/logo-mark-on-dark.svg` | Used in the fixed bar next to the text name. |
| Logo mark (light background) | `src/MyGarage.Web/wwwroot/images/logo-mark-on-light.svg` | For light surfaces and documentation. |
| Favicon (compact icon form) | `src/MyGarage.Web/wwwroot/images/favicon.svg` | Linked from the document head. |
| Brand tokens | `src/MyGarage.Web/wwwroot/app.css` | CSS custom properties; values listed in [research.md](research.md) R3. |
| UI standard | `docs/ui-standard.md` | Logo usage (minimum size, clear space, color variations), palette, typography, spacing, page structure, component patterns, trail/title rules, icon attribution. |

## Relationships and state transitions

- A page owns exactly one `PageHeader`; the `PageHeader` owns its ordered `Crumb` list.
- The navigation bar has two states (signed out, signed in) driven only by authentication state; there are no other transitions. Dropdown open/closed state lives in the browser (Bootstrap), not on the server.
