# Quickstart: Window Sticker Upload Shortcut

Validation guide. Contracts: [API](contracts/api.openapi.yaml), [web routes](contracts/web-routes.md). Data: [data-model.md](data-model.md). Decisions: [research.md](research.md).

## Prerequisites

- .NET 10 SDK (pinned by `global.json`) and Docker or Podman running (Testcontainers PostgreSQL; Aspire PostgreSQL).
- Branch `010-window-sticker-upload` checked out. No migration is added, so an existing local database needs no change.
- For the manual AI checks only: the Azure OpenAI deployment already configured for email intake (`EmailIntake:Extraction:Endpoint` and `:Deployment` in the AppHost user-secrets), with your account holding `Cognitive Services OpenAI User` on it. Without them the import answers "Reading stickers is not set up here" and the rest of the app works.
- A few real window stickers: a clear PDF, a phone photo (JPEG), a PNG screenshot, a sticker that lists many options, and one that lists only a few details. Non-sticker files: a text file renamed `.pdf`, an 11 MB file, a photo of something else.
- Optional: `StickerImport:TimeoutSeconds` (default 90) in the AppHost settings.

## Automated checks

Do not run tests while `aspire run` is active (file locks).

```powershell
dotnet build MyGarage.slnx                      # must finish with 0 warnings
dotnet test tests\MyGarage.Database.Tests       # sticker feature application: replace, exact and key duplicates, 100 limit, ownership, atomic rollback
dotnet test tests\MyGarage.Api.Tests            # extraction endpoint (success, partial, no details, no file, wrong type by signature, oversize, other user's vehicle, 401, 429, extractor down),
                                                # mapper (validation drop, lookup matching, duplicate detection, caps), extractor client with a fake handler,
                                                # vehicle create/update with StickerFeatures, existing email-intake extractor tests (refactor guard)
dotnet test tests\MyGarage.Web.Tests            # tabs for add and edit, import round trip (SSR) for add and edit, results panel, features group, UI conformance (no Docker needed)
```

Expected: all green; no migration runs.

## Manual validation (Aspire)

```powershell
aspire run
```

Sign in, then (US = user story, SC = success criterion in the spec):

1. **Add form tabs (US1)**: open Add Vehicle. Tabs are "Import from sticker" then "Details", and it opens on Import from sticker. The Details tab and a normal save work without importing.
2. **Edit form tabs (US1)**: open an existing vehicle's Edit page. Tabs are Import from sticker, Details, Media, Description, and it opens on Details; `?tab=import` opens Import from sticker.
3. **Choose and see (US1)**: choose a sticker PDF: an embedded view appears; choose an image: a preview appears; choose another file: the first preview disappears. Accept import is disabled until a file is chosen. Nothing is requested from the API yet (browser network tab shows no call until Accept).
4. **Accept on the add form (US1, SC-001, SC-003)**: press Accept import. The button shows progress, then the Details tab opens with year, make, model, trim, VIN, engine, horsepower, color, seats, doors, wheel and tire size filled where the sticker prints them, and matching lookup dropdowns selected. The Import pane says how many details were read. Change one value, add ownership and purchase date, and save; the detail page shows all values. Time from page open to saved vehicle is under 2 minutes (SC-002).
5. **Unmatched lookups (US1)**: with a sticker whose body style is not in your Vehicle Types, the dropdown stays unset and a hint "The sticker says: ..." appears; Preferences shows no new item.
6. **Features on add (US1)**: the "Features from the sticker" group lists the sticker's options, all ticked. Untick two, save: the vehicle has only the ticked ones.
7. **Edit with consolidation (US1, SC-005)**: give a vehicle the features "Sunroof" and "heated seats", then import a sticker that lists "Panoramic Sunroof" and "Heated Seats". "Panoramic Sunroof" is proposed with "Replaces: Sunroof"; "Heated Seats" is the same feature as "heated seats" ignoring case, so the vehicle ends with one such feature, not two (the shared spelling is kept, per the spec note). Save: the vehicle shows "Panoramic Sunroof" and no "Sunroof". Repeat leaving the "Panoramic Sunroof" proposal unticked: "Sunroof" stays.
8. **Edit overlay (US1)**: on an edited vehicle, values the sticker provides replace the form values; values it does not provide keep the saved ones. Type an unsaved change, then import: the unsaved change is gone, as the pane warned.
9. **Nothing is saved without Save (US1, SC-005)**: import, then leave without saving: the vehicle (or its details and features) is unchanged; no sticker file exists anywhere (container logs and temp folder show nothing kept).
10. **Failures (US2, SC-004)**: an unsupported file, a `.pdf` that is really text, an 11 MB file, a non-sticker photo, and a blurred photo each give a specific message and leave the form unchanged. Stop the AI settings and import: "Reading stickers is not set up here." A tenth import within a minute gives the "several imports" message.
11. **Duplicate VIN (US2)**: import a sticker whose VIN matches another of your vehicles: a notice links to that vehicle and adding is still allowed.
12. **Privacy (US3, SC-006)**: as a second user, request the first user's edit page with the same import: not found. Inspect the API and Web logs: no VIN, no feature names, no file name. Check the browser address bar after an import: no query values.
13. **Phone and keyboard (FR-016)**: on a narrow phone-sized window, the pane works without horizontal scrolling and can be used entirely by keyboard; on a phone, the file input offers the camera.
14. **No script**: disable JavaScript: choosing a file and pressing Accept import still works (no preview).
