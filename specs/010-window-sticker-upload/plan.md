# Implementation Plan: Window Sticker Upload Shortcut

**Branch**: `010-window-sticker-upload` | **Date**: 2026-11-05 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/010-window-sticker-upload/spec.md` (including Amendment 1)

## Summary

The vehicle add and edit forms gain an **Import from sticker** tab (first tab; the add form opens on it, the edit form still opens on Details). The user chooses a PDF or image of the window sticker and sees it in the tab (a client-side preview; a name and size without a script), then presses **Accept import**. That posts the file to the same page, which forwards it to one new API endpoint, `POST /api/vehicles/sticker-extractions`. The API checks the file by its signature, asks the Azure OpenAI Responses API (PDF or image input, strict JSON schema, already used for email attachments) to read the sticker, then validates, normalizes, and maps what came back onto the user's own lookup items, and returns a `StickerImportResultDto`. The page overlays the values on the form model (a new vehicle's empty form, or an edited vehicle's stored values), opens the Details tab, and lists what was read, what was not, and what was ignored. Nothing is saved and nothing is stored: the file lives only in the request, and the extraction result lives only in the rendered form.

Features read from the sticker are shown on the Details tab as proposals (selectable, selected by default) next to the vehicle's existing features they would replace. For an edit, the API passes the vehicle's feature names to the model so it can name near-duplicates (for example "Sunroof" and "Panoramic Sunroof"); code adds exact duplicates (equal ignoring case, spacing, and punctuation) and keeps only replacements that really are features of that vehicle. The selected proposals travel with the normal vehicle save as a new optional `StickerFeatures` list in `VehicleRequest`; the API applies them in the same transaction as the vehicle write: link the sticker's feature, unlink the features it replaces, never exceed 100.

Technical approach: no migration and no new table or storage container. The existing Responses transport in `ResponsesApiExtractor` is factored into a shared `Common/ResponsesApiClient` and reused by a new `StickerExtractor` (own instructions and schema). A per-user rate limit and a short timeout guard the interactive, paid call. The Web layer is static SSR like the rest of the app: a second named form on the page, a small progressive-enhancement preview module, and a dedicated API client with its own long-timeout, no-retry pipeline.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (pinned by `global.json`)

**Primary Dependencies**: Existing: Aspire 13.x, `Microsoft.Identity.Web`, FluentValidation, ASP.NET Core OpenAPI and rate limiting (built in), Npgsql, Dapper, `Microsoft.Extensions.Http.Resilience`, Serilog, OpenTelemetry, Bootstrap 5.3.x, `DefaultAzureCredential` (through the existing Azure SDK dependencies). **New**: none (research R1, R4).

**Storage**: PostgreSQL, existing tables only (`vehicles`, `features`, `vehicle_tags`, lookup tables); new SQL scripts under `Scripts/Tags/` for applying sticker features. No migration, no blob container: the uploaded file is never persisted (it is buffered by the web server for the duration of the request and deleted with it).

**Testing**: xUnit; Testcontainers PostgreSQL for `MyGarage.Database.Tests` and `MyGarage.Api.Tests` (no mocked database); a fake `IStickerExtractor` and a fake `HttpMessageHandler` for the AI boundary; bUnit and `WebApplicationFactory` (SSR round trip with AngleSharp) for `MyGarage.Web.Tests`; `UiStandardConformanceTests` covers the new `.razor` files. Docker required for database and API tests.

**Target Platform**: Local development with Aspire; Azure Container Apps (Web up to 2 replicas, which is why no state is kept in Web memory) with the Azure OpenAI deployment already used for email intake.

**Project Type**: Web application (Blazor Web App static SSR + minimal API + PostgreSQL), Aspire-orchestrated.

**Performance Goals**: Form filled in within 30 s for 95% of readable stickers (SC-003); user sees progress while waiting. Extraction call bounded at 90 s (configurable); page shows the "unavailable" message beyond it.

**Constraints**: File at most 10 MB; PDF, JPEG, PNG, WebP identified by signature. Authenticated user endpoint only (app-only tokens refused by the existing fallback policy). Ownership of an edited vehicle checked in SQL before its features are read. Per-user rate limit of 6 extractions per minute. Model gets no tools and no data except the sticker and, on an edit, that vehicle's own feature names. No sticker content (VIN, features, extracted values) in logs or URLs; results are never put in a redirect. All SQL parameterized in `.sql` files; async with `CancellationToken`.

**Scale/Scope**: A handful of imports per user per vehicle. One new endpoint, one new rate-limit policy, one shared transport class (refactor), one new extractor, one new tab and results panel, one new JS module, one new typed Web client, one field added to `VehicleRequest`, two SQL scripts.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. .NET 10 / C# only; nullable on; warnings as errors; async + `CancellationToken` | PASS | Extractor, endpoint, repository method, and page handler are async and forward tokens. The preview module is small, optional JavaScript like the existing `dropzone.js`. |
| II. Feature-folder organization | PASS | New `Vehicles/StickerImport/` folders in Shared, Api, Web (and mirrored in tests); the shared Responses transport goes in `Api/Common/`; feature SQL in `Scripts/Tags/` beside the tag scripts it extends. |
| III. Solution structure & dependency direction | PASS | No new projects or references. Web reaches the data only through the Api; Database does not reference Shared (the request is mapped to a Database-owned `StickerFeatureWrite` record in the Api). |
| IV. DTO boundary | PASS | `StickerImportResultDto`, `StickerFeatureDto`, and `StickerFeatureRequest` live in `MyGarage.Shared`; explicit mapping, tested. |
| V. Validated minimal APIs | PASS | The new endpoint is in the existing vehicles group, requires authorization, validates the upload with a filter (presence, size, signature) before any model call, declares status codes (200, 400, 404, 413, 429, 503), and returns `ProblemDetails`. `StickerFeatureRequest` has a FluentValidation validator wired into `VehicleRequestValidator`. |
| VI. Dapper, PostgreSQL, SQL scripts, private blobs | PASS | Feature writes are parameterized `.sql` files run through a repository; ownership is proved in SQL (`Tags/LockVehicle`) before any vehicle's tags are read or changed. No blob is created. |
| VII. Versioned migrations | PASS | No schema change, so no migration (research R5). |
| VIII. Test-first, real dependencies | PASS | Failing tests first for the validator, extractor client (fake handler), result mapper (lookup matching, sanitizing, duplicate detection), endpoint (success, validation failure, wrong type, oversize, not found, 401, 429, extractor down), repository (consolidation, limit, ownership), and Web (tabs, import round trip, results panel). Real PostgreSQL; only the external AI service is faked. |
| Auth: Entra OIDC + JWT; every endpoint authorized | PASS | Standard user policy; no anonymous endpoint and no app role. |
| Observability | PASS | Logs carry outcome codes, counts, and durations only. New meter `MyGarage.StickerImport`: extractions by outcome and duration. |
| Data protection | PASS | Owner comes from claims; the edited vehicle's features are loaded owner-scoped; the file and the result are visible only to the requesting user because they never leave the request/response; no cross-user data. |
| Dependencies justified | PASS | No new packages. |

**Post-design re-check (after Phase 1)**: unchanged. The one cross-feature change (refactoring the Responses transport that email intake uses into `Common/`) is behavior-preserving and tracked below.

## Project Structure

### Documentation (this feature)

```text
specs/010-window-sticker-upload/
├── plan.md              # This file
├── spec.md
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── api.openapi.yaml # sticker-extractions endpoint; StickerFeatures on VehicleRequest
│   └── web-routes.md    # tab, forms, results panel, preview module, client behavior
├── checklists/requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── README.md                                    # + StickerImport:TimeoutSeconds setting
├── MyGarage.AppHost/AppHost.cs                  # + StickerImport settings passed to the api (optional)
├── MyGarage.Shared/Vehicles/
│   ├── StickerImport/                           # StickerImportResultDto, StickerFeatureDto, StickerImportOutcome, StickerNoticeDto,
│   │                                            # StickerImportLimits (max bytes, accepted types)
│   └── VehicleRequest.cs                        # + StickerFeatures (IReadOnlyList<StickerFeatureRequest>?), StickerFeatureRequest
├── MyGarage.Database/
│   ├── Tags/VehicleTagRepository.cs             # + ApplyStickerFeaturesAsync (shared connection/transaction with the vehicle write)
│   ├── Tags/StickerFeatureWrite.cs              # record: Name, ReplacesTagIds
│   ├── Vehicles/VehicleRepository.cs            # + InsertWithFeaturesAsync/UpdateWithFeaturesAsync (vehicle write and feature writes in one transaction)
│   └── Scripts/Tags/                            # DeleteByIds.sql (reuse ListByVehicle, LockVehicle, UpsertFeature, Insert, Exists, CountForVehicle)
├── MyGarage.Api/
│   ├── Common/
│   │   ├── ResponsesApiClient.cs                # shared transport (token, request envelope with streamed document, error classification, answer text)
│   │   └── StickerImportRateLimiting.cs         # per-user fixed window policy
│   ├── EmailIntake/Extraction/ResponsesApiExtractor.cs  # now delegates transport to ResponsesApiClient (same behavior)
│   └── Vehicles/StickerImport/
│       ├── StickerImportEndpoints.cs            # POST /api/vehicles/sticker-extractions (multipart), upload filter, metrics
│       ├── IStickerExtractor.cs                 # IsEnabled, ExtractAsync(StickerInput)
│       ├── StickerExtractor.cs                  # Responses call with StickerSchema
│       ├── StickerSchema.cs                     # instructions + strict JSON schema
│       ├── StickerImportOptions.cs              # StickerImport: TimeoutSeconds (45)
│       ├── StickerResultMapper.cs               # sanitize via VehicleRequestValidator, lookup matching, duplicate detection, caps
│       ├── FeatureKey.cs                        # normalization used for duplicate detection
│       └── StickerFeatureRequestValidator.cs    # names, count, uniqueness (used by VehicleRequestValidator)
└── MyGarage.Web/
    ├── Vehicles/
    │   ├── VehicleTabs.cs                       # + Import tab id/label/icon; create tab list; first-tab rule
    │   ├── VehicleForm.razor                    # + Import pane (second form), + "Features from the sticker" group inside the details form
    │   ├── VehicleCreate.razor, VehicleEdit.razor  # + import-sticker named form and ImportAsync handler
    │   ├── VehicleFormModel.cs                  # + StickerFeatures (choices), + From/overlay helpers, ToRequest carries selected features
    │   └── StickerImport/                       # StickerImportPanel.razor, StickerResultsPanel.razor, StickerImportApiClient.cs,
    │                                            # StickerHints.cs (field -> unmatched text), StickerImportMessages.cs
    └── wwwroot/js/sticker-preview.js            # optional preview of the chosen file, object URL revoked on change/unload

tests/
├── MyGarage.Database.Tests/Tags/               # sticker feature application: replace, duplicates, limit, ownership, atomicity
├── MyGarage.Api.Tests/Vehicles/StickerImport/  # endpoint matrix, extractor client (fake handler), mapper, validator, rate limit, refactor guard (existing intake tests)
├── MyGarage.Web.Tests/Vehicles/                # tabs, panels, SSR round trip for add and edit, UI conformance
└── MyGarage.AppHost.Tests/                     # unchanged
```

**Structure Decision**: One new feature subfolder `StickerImport/` under `Vehicles/` in Shared, Api, and Web, matching how `Images/`, `Tags/`, and `JournalNotes/` already sit under `Vehicles/`. Feature persistence stays with the tags it modifies. The Responses transport is cross-feature (email intake and stickers), so it goes to `Common/`.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Refactor of `ResponsesApiExtractor` so its HTTP and token plumbing moves to `Common/ResponsesApiClient` (touches feature 007 code) | A second feature needs the same Entra token, document-streaming request body, error classification, and answer-text extraction | Copying about 150 lines would leave two places to fix when the service API changes. Existing intake extractor tests guard the refactor; no behavior change. |
| A second named form on the add/edit pages and a dedicated typed API client with its own resilience pipeline | The upload is a different request from the vehicle save, and the extraction call is slow and paid, so it must not use the default 10 s attempt timeout or automatic retries | A single form cannot carry a file for reading and the vehicle fields for saving independently; the default pipeline would time out or retry (double billing). |
| `StickerFeatures` added to `VehicleRequest` (shared by create and update) | Features must be saved with the vehicle, and an added vehicle has no id yet for the existing `/tags` endpoints | A second call after create would leave a saved vehicle without its features if it failed, and would make the consolidation non-atomic. |

## Amendment 3: feature categories

- **Storage**: migration `0014_add_feature_category.sql` adds a nullable `category` column to the shared `features` table (null means not yet categorized; it is shown under Other). Written once (`UPDATE ... WHERE category IS NULL`). The fixed list lives in `MyGarage.Shared/Vehicles/FeatureCategories`; the Database project stores any text and the API only ever writes a name from that list.
- **Categorizing**: `Api/Vehicles/Tags/FeatureCategorizer` calls the shared `ResponsesApiClient` with no document and a strict schema whose `category` is an enum of the fixed list, answering by the number of each name. `FeatureCategorizationService.CategorizePendingAsync` takes up to 100 features without a category (oldest first), asks once, and stores the valid answers; it never throws for a service or database failure. It runs inline after `POST /api/vehicles/{id}/tags` when the new feature has no category, and inside `VehicleService.CreateAsync/UpdateAsync` when sticker features were saved. `FeatureCategorizationSweep` (a `BackgroundService`, every 10 minutes, `FeatureCategories:SweepEnabled`) backfills and retries. The HTTP client has one attempt of 40 s and no retry; the model call is bounded by `FeatureCategories:TimeoutSeconds` (30).
- **Contract**: `VehicleTagDto` gains `Category` (a name from the list; Other while uncategorized). No request changed.
- **Web**: `VehicleTagsView` shows one card per category (`FeatureGroups.Build`, first 7 plus a native `<details>` for the rest) with a GET search form (`featureQuery`, kept on the Description tab) handled by `VehicleDetail`; `VehicleTagsSection` shows a category badge per feature. No script.
- **Tests**: Database (migration, pending list, set once), Api (categorizer parsing and request, service and endpoint behavior with `FakeFeatureCategorizer`), Web (`FeatureGroups` and the cards, search, and the edit badge).
