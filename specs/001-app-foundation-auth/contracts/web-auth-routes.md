# Contract: Web Authentication Routes

All routes are served by `MyGarage.Web`. Every other route requires an authenticated user (fallback authorization policy).

| Route | Method | Anonymous | Behaviour |
|---|---|---|---|
| `/authentication/login` | GET | yes | Query `returnUrl` (optional). Accepts only a local relative path starting with a single `/`; anything else becomes `/`. Issues an OIDC challenge and returns to the validated URL. |
| `/authentication/logout` | POST | no | Clears the local cookie and signs out of the identity provider; redirects to `/authentication/signed-out`. POST only, with antiforgery. |
| `/authentication/signed-out` | GET | yes | Friendly confirmation and a "Sign in" link. |
| `/authentication/error` | GET | yes | Query `reason` in {`cancelled`, `denied`, `unavailable`, `unknown`}; shows a fixed friendly message per reason (never echoes raw provider error text) and a "Try again" link. |
| `/signin-oidc`, `/signout-callback-oidc` | - | yes | Handled by the OIDC middleware; registered as the redirect/post-logout URIs in Entra. |
| `/` | GET | no | Home: greets the user by display name (`name` claim). |
| `/api-check` | GET | no | Static SSR page; calls API `GET /api/users/me` and `GET /api/sample-data` with the user's access token and renders results or a friendly error if the API is unavailable. |
| `/health`, `/alive` | GET | yes (Development) | Aspire health endpoints; no user data. |

## Scope requested for the API

`api://<AzureAd:Api:ClientId>/access_as_user`

## Failure mapping

| Cause | Reason shown |
|---|---|
| User cancels at Entra | `cancelled` |
| Account outside tenant / access denied | `denied` |
| Identity provider unreachable / remote failure | `unavailable` |
| Anything else | `unknown` |
