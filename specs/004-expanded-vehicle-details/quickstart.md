# Quickstart: Validate Expanded Vehicle Details and Lookup Preferences

Run guide for validating the feature once implemented. Details: [contracts/](contracts/), [data-model.md](data-model.md).

## Prerequisites

- Everything from [feature 002's quickstart](../002-vehicle-tracking/quickstart.md) (SDK, Aspire CLI, Entra registrations, Docker or Podman running)
- At least one vehicle saved before this feature (use the existing local database volume) to check upgrade behavior, plus a second Entra user for isolation checks

## 1. Start everything

```powershell
aspire run
```

Expected: the dashboard shows `postgres`, `garagedb`, `storage`, `blobs`, `api`, and `web` Running/Healthy. The `api` logs list `0005_create_lookup_items` and `0006_add_vehicle_details` as applied once; a restart applies none.

## 2. Verify (manual)

1. **Upgrade (US4)**: Open `https://localhost:7080/vehicles` and sign in. Existing vehicles still appear. Open one: Basic Info and Specifications show the new fields as "Not set", Mileage, Color, and Doors are unchanged on the Specifications tab, and a prompt asks for a year.
2. Edit that vehicle and save without a year: the save is rejected with a year message and the Basic Info tab opens. Add a year and save.
3. **Defaults (US2)**: Add a new vehicle. On Basic Info open Vehicle Type (Sedan ... Motorcycle); on Specifications open Fuel Type (Gasoline, Diesel, Electric, Hybrid), Transmission (Automatic, Manual), and Drivetrain (FWD, RWD, AWD, 4WD). A brand-new user must see these without visiting Preferences.
4. **Fields and validation (US1)**: Fill every field on both tabs and save; confirm the detail page shows each value on the same tab. Then try: a VIN with 16 characters, a VIN containing `O`, year 1850 or next-next year, horsepower 0, seats 25, wheel size 5 or 17.55, a future Date Purchased, Ownership "Purchased" with a Date Lease Ends, and a lease end before the purchase date. Each must be rejected with a message beside its field, the tab holding the first error must open, and nothing is saved. Enter a lower-case VIN and confirm it is stored upper case.
5. **Preferences (US3)**: Open the account menu and choose Preferences. Confirm the display name and email, and four tabs (Vehicle Types, Fuel Types, Transmissions, Drivetrains). Visit each; use `?tab=drivetrains` directly; try `?tab=bogus` (first tab opens).
6. **Customize (US2)**: On Vehicle Types hide "Wagon" and add "Cargo Van". Reopen the vehicle form: Wagon is gone and Cargo Van follows the defaults. Add "cargo van" again (duplicate message) and " sedan " (duplicate of a hidden/shown default). Hide "Sedan", add it back via Show. Add an empty and a 31-character name (rejected).
7. **Hidden but in use (US2.6)**: Save a vehicle with Vehicle Type "Coupe", hide "Coupe" in Preferences, reopen that vehicle: the detail page and card still show Coupe; the edit form shows "Coupe (hidden)" selected; a new vehicle form does not offer Coupe.
8. **Garage (US5)**: Cards show `year make model`. Search a year, part of a VIN, and part of an engine; use the filters for vehicle type, fuel type, transmission, drivetrain, and year range; set min year greater than max year and confirm a message. Confirm filter choices list only items used by your vehicles (including hidden ones).
9. **Isolation (US6)**: Sign in as the second user: their lookups show only the defaults (no Cargo Van, nothing hidden), and their garage is empty. Opening the first user's vehicle URL shows not found. In the `api` logs confirm no VIN or custom item name appears.
10. Restart (`aspire run` again): all vehicles, custom items, and hidden choices persist.

## 3. Verify the API directly

```powershell
curl.exe -k -i https://localhost:<api-port>/api/lookups                       # 401 application/problem+json
curl.exe -k -i -X POST https://localhost:<api-port>/api/lookups/fuel-types    # 401 without a token
```

(`<api-port>` is on the dashboard; with a valid token `GET /api/lookups` returns the four lists, `GET /api/lookups?includeHidden=true` also returns hidden items, and `/api/lookups/bogus` returns 404.)

## 4. Automated tests

```powershell
dotnet test MyGarage.slnx
```

Expected: all pass with zero build warnings and Docker running. Covers migrations 0005/0006 on an empty database and over existing vehicle rows, default items and order, lookup add/duplicate/limit/hide/show with owner isolation, vehicle persistence of every new column and constraint, validators and mappers, search and filter additions, endpoint success/validation/not-found/unauthenticated/cross-user paths, the Preferences page and menu entry, the tabbed vehicle forms and detail page, cards with year, and the UI standard conformance checks.
