# Quickstart: Validate Vehicle Tracking

Run guide for validating the feature once implemented. Details: [contracts/](contracts/), [data-model.md](data-model.md).

## Prerequisites

- Everything from [feature 001's quickstart](../001-app-foundation-auth/quickstart.md) (SDK, Aspire CLI, Entra registrations already created)
- Docker (or Podman) running: required for the PostgreSQL container, the Azurite storage emulator, the Testcontainers-based tests, and the AppHost smoke test

## 1. Start everything

```powershell
aspire run
```

Expected: the dashboard shows `postgres`, `garagedb`, `storage`, `blobs`, `api`, and `web` Running/Healthy. The API applies pending migrations and ensures the `vehicle-images` container exists at startup (check the `api` logs for the applied script ids; a rerun applies none).

## 2. Verify the garage (manual)

1. Browse to `https://localhost:7080/vehicles` and sign in. A new user sees the empty state with "Add vehicle" (US1).
2. Add a vehicle (nickname, make, model, trim, color, mileage, doors). Submit with a blank nickname and a mileage of -5 first and confirm per-field messages and that nothing is saved.
3. Add 13 or more vehicles (vary make, color, doors, mileage) so the garage shows 12 cards as a card grid (not a table) with a pager; use Next/Previous; request `/vehicles?page=999` and confirm it shows the last page (US1).
4. Search and filter (US2): type part of a nickname, a model, a color, and a tag and confirm matches only; search for `%` or `_` and confirm they are matched literally (no wildcard behaviour); open Filters and choose make, color, doors, tag, and a mileage range; confirm criteria are shown as removable badges, a result count is shown, page links keep the criteria, a no-match search shows "No vehicles match" with "Clear all", and min mileage greater than max shows a message. Confirm the filter choices list only values from your own garage.
5. Open a vehicle; edit it; confirm the change is shown on the detail page and the card (US3). Delete a vehicle after confirming; cancel once and confirm nothing is removed.
6. Add tags such as "sunroof"; add " SUNROOF " and confirm the duplicate message; remove a tag (US4).
7. Add notes with categories you make up (for example "Oil change", "Road trip") and one with the category left blank; confirm newest first, the category badge (none for the blank one), and the date; add more than 10 to see the journal pager; edit and delete a note (US5).
8. Upload a JPEG/PNG/WebP; confirm it appears and becomes the card picture; upload a second and set it main; try a renamed text file (`.jpg`) and a file over 5 MB and confirm rejection messages; delete the main image and confirm another takes over (US6). In the Azurite container (or Storage Explorer attached to the emulator) confirm blobs appear under `vehicle-images/{vehicleId}/` and disappear when the image or the vehicle is deleted.
9. Stop and start (`aspire run` again): confirm all vehicles, tags, notes, and images are still there (SC-007).

## 3. Verify isolation

Sign in as a second user (a second tenant account) and confirm the garage is empty and the filter choices are empty. Copy a vehicle URL from the first user's session and open it as the second user: it must show not found, as must a copied image URL. Searching for a first-user nickname must return nothing.

## 4. Verify the API directly

```powershell
curl.exe -k -i https://localhost:<api-port>/api/vehicles         # 401 application/problem+json
curl.exe -k -i "https://localhost:<api-port>/api/vehicles?q=truck&minMileage=1000"   # 401 without a token
```

(`<api-port>` is on the dashboard; with a valid token the calls return a `VehicleSummaryPage`.)

## 5. Automated tests

```powershell
dotnet test MyGarage.slnx
```

Expected: all pass with zero build warnings and Docker running. Covers migrations on an empty database (and checksum drift), repositories with owner isolation, paging and out-of-range handling, the search/filter matrix and literal handling of `%`/`_`, tag uniqueness/limits, note ordering and free-text categories, image signature/size/limit rules with blob create/delete and rollback on storage failure, endpoint success/validation/not-found/unauthenticated paths, cross-user 404s, card, filter, and pager components, image proxy, and AppHost smoke including the database and storage resources.
