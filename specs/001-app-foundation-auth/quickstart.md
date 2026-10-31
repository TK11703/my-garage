# Quickstart: Validate Application Foundation and User Sign-In

Run guide for validating the feature once implemented. Details: [contracts/](contracts/), [data-model.md](data-model.md).

## Prerequisites

- .NET SDK 10.0.x, Aspire CLI, Azure CLI, PowerShell 7+
- A Microsoft Entra tenant where you may create app registrations
- Trusted dev certificate: `dotnet dev-certs https --trust`

## 1. Register the apps in Entra

```powershell
az login --tenant <tenant-id>
./infra/entra/Initialize-Entra.ps1            # add -WebPort if you changed the default
```

Expected: summary of tenant and client IDs; no secret printed; running it again changes nothing. See [contracts/entra-setup-script.md](contracts/entra-setup-script.md).

## 2. Start everything

```powershell
aspire run
```

Expected: the dashboard opens; `api` and `web` show Running/Healthy within 60 seconds (SC-004, SC-005).

## 3. Verify sign-in (manual)

1. Browse to `https://localhost:7080` -> redirected to Microsoft sign-in.
2. Sign in with a tenant account -> home page shows your display name (US1).
3. Open `/api-check` -> shows your identity from the API and the fixed sample items (US3).
4. Use Sign out -> signed-out page; revisiting `/` prompts sign-in again (US2).
5. Cancel sign-in, or use an account from another tenant -> friendly error page with "Try again".

## 4. Verify API protection

```powershell
curl.exe -k -i https://localhost:<api-port>/api/users/me        # 401 application/problem+json
```

(`<api-port>` is shown on the dashboard.)

## 5. Automated tests

```powershell
dotnet test MyGarage.slnx
```

Expected: all pass with zero build warnings. Covers valid/invalid/expired/wrong-audience tokens, spoofed user ids ignored, validation failures, return-URL handling, greeting display, and AppHost health smoke test.
