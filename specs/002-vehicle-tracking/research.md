# Research: Vehicle Tracking

No `NEEDS CLARIFICATION` items remained in the Technical Context; the entries below record design decisions.

## R1. PostgreSQL and Azure Storage in the Aspire AppHost

- **Decision**: `AppHost` adds `builder.AddPostgres("postgres").WithDataVolume()` with `.AddDatabase("garagedb")`, and `builder.AddAzureStorage("storage").RunAsEmulator(...)` (Azurite container with a persistent data volume) with `.AddBlobs("blobs")`. The `api` project uses `WithReference(garageDb).WithReference(blobs)` and `WaitFor` for both. The Api registers `AddNpgsqlDataSource("garagedb")` and `AddAzureBlobServiceClient("blobs")` (Aspire client integrations: configuration, health checks, tracing). `web` references neither.
- **Rationale**: One-command start, dashboard visibility, and no hand-written connection strings (extends feature 001 R1). The generated PostgreSQL password is stored by Aspire in AppHost user-secrets so it is stable across runs and matches the persisted volume. Data volumes keep data across restarts (SC-007 in development). When published to Azure, the same resource name `blobs` binds to a real storage account.
- **Alternatives**: Developer-installed PostgreSQL or a real storage account for local work (manual setup, cost, no dashboard); containers without volumes (data lost on restart).
- **New packages (justified)**: `Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.Azure.Storage`, `Aspire.Npgsql`, `Aspire.Azure.Storage.Blobs` (versions match the existing Aspire 13.x pin; `Azure.Storage.Blobs` arrives transitively).

## R2. Data access: Npgsql, Dapper, SQL files

- **Decision**: `MyGarage.Database` references `Npgsql` and `Dapper` only (plus `Microsoft.Extensions.*` abstractions for DI and logging). Every statement lives in an embedded `.sql` resource under `Scripts/<Feature>/` (for example `Scripts/Vehicles/GetById.sql`). A singleton `SqlScriptStore` reads a resource once (by `"<Feature>/<Name>"`) and caches the text. Repositories (`VehicleRepository`, `VehicleTagRepository`, `JournalNoteRepository`, `VehicleImageRepository`) open a connection from the injected `NpgsqlDataSource`, execute with Dapper `CommandDefinition` (always passing the `CancellationToken`), and return plain records. Unit of work is a single transaction inside a repository method; the Api never sees a connection.
- **Rationale**: Mandated by constitution VI; embedded resources keep SQL out of C# strings and deploy with the assembly. Column-to-property mapping uses `DefaultTypeMap.MatchNamesWithUnderscores = true`.
- **Alternatives**: EF Core (prohibited); runtime file loading from disk (fragile deployment); Dapper without a script store (SQL literals in C#, prohibited).
- **New packages (justified)**: `Dapper` (required by the constitution). `Npgsql` arrives transitively through `Aspire.Npgsql` in the Api and is referenced directly by Database. `MyGarage.Database` also references `Azure.Storage.Blobs` for the image store (R9).

## R3. Schema migrations

- **Decision**: Forward-only scripts `Scripts/Migrations/NNNN_<name>.sql` embedded in `MyGarage.Database`:
  `0001_create_vehicles`, `0002_create_vehicle_tags`, `0003_create_journal_notes`, `0004_create_vehicle_images`. A `MigrationRunner` (a) takes a PostgreSQL advisory lock so concurrent starts do not race, (b) ensures the ledger table `schema_migrations(script_id text primary key, checksum text not null, applied_at timestamptz not null)` exists, (c) fails if any applied script's SHA-256 checksum differs from the embedded one, (d) applies each pending script in order inside its own transaction and records it in the ledger in the same transaction. The Api runs the runner from a hosted service during startup, before it accepts requests; the migration tests call it directly against a fresh container.
- **Rationale**: Constitution VII. Running at Api startup keeps the one-command developer experience; the runner API is separate from the host so a deploy-time job can call it later.
- **Alternatives**: A separate migrator project/resource (extra moving part for v1); tool-based migrations such as DbUp/FluentMigrator (new dependency, constitution requires a custom runner).

## R4. Ownership model and not-found semantics

- **Decision**: `vehicles.owner_id` (text) holds the token's user id (`oid`, fallback `sub`, exactly as `CurrentUserMapper` derives it). An Api `CurrentOwner` helper reads it from the validated `ClaimsPrincipal`; endpoints never accept an owner id. Every vehicle statement includes `WHERE owner_id = @OwnerId`. Every child statement (tags, notes, images) filters through the parent: `... WHERE id = @ChildId AND vehicle_id = @VehicleId AND EXISTS (SELECT 1 FROM vehicles WHERE id = @VehicleId AND owner_id = @OwnerId)` (or `INSERT ... SELECT ... FROM vehicles WHERE ...`). A statement that matches no row yields `null`/`false`, which the Api turns into a `404 ProblemDetails`, identical for "does not exist" and "belongs to someone else".
- **Rationale**: Constitution data-protection rules and FR-013/FR-014; SC-003 is tested with two users.
- **Alternatives**: Loading then checking owner in C# (violates "scoped by owner ID in SQL"); returning 403 for foreign records (discloses existence).

## R5. Identifiers and timestamps

- **Decision**: Primary keys are `uuid` generated by the Api with `Guid.CreateVersion7()` (time-ordered, not enumerable). Timestamps are `timestamptz` read/written as `DateTimeOffset` (UTC). `created_by` / `updated_by` columns store the owner id of the acting user (FR-017).
- **Rationale**: Constitution time rules; non-sequential ids avoid trivially guessable addresses (defence in depth; the SQL owner filter remains the control).
- **Alternatives**: `bigint identity` (guessable, leaks counts).

## R6. Paging

- **Decision**: Offset paging with `page` (1-based) and `pageSize`. Vehicles default 12 per page, notes default 10 per page, max 50. The list statement orders by `lower(nickname), id` (stable and deterministic, FR-002a) and the Api returns `PagedResultDto<T> { Items, Page, PageSize, TotalCount, TotalPages }`. Search and filters (R16) are applied in the same statement before `LIMIT/OFFSET`, and `TotalCount` is the filtered count. If `page` exceeds the last page, the Api clamps to the last valid page and returns that page number; `page < 1` or `pageSize` outside 1-50 is a 400 validation problem. Notes order by `created_at desc, id desc`. A supporting index `(owner_id, lower(nickname), id)` backs the garage list.
- **Rationale**: Garages are small (tens of vehicles); offset paging is simple and supports "page X of Y" UI. Clamping implements the spec's "nearest valid page" edge case.
- **Alternatives**: Keyset paging (no total/page-number UI, unnecessary at this scale).

## R7. Tags

- **Decision**: Table `vehicle_tags(id, vehicle_id, name, name_key, created_at)` with `name` as the trimmed display value, `name_key = lower(trim(name))` (computed generated column) and a unique index on `(vehicle_id, name_key)`. Tags are a sub-resource: `POST /api/vehicles/{id}/tags` and `DELETE /api/vehicles/{id}/tags/{tagId}`. Adding runs in one transaction that locks the parent vehicle row (`SELECT ... FOR UPDATE` filtered by owner), checks the 20-tag limit, then inserts. Duplicate and limit-reached outcomes return `409 ProblemDetails`. Validation: trimmed, 1-30 characters, no control characters.
- **Rationale**: Relational tags allow future filtering/search by tag (out of scope now) and make per-vehicle uniqueness a database guarantee; row lock makes the count check race-free.
- **Alternatives**: `text[]` column on `vehicles` (harder uniqueness/ownership statements and future querying).

## R8. Journal notes and free-text categories

- **Decision**: Table `journal_notes(id, vehicle_id, category, body, created_at, created_by, updated_at, updated_by)`. `category` is a nullable `varchar(40)`: trimmed free text chosen by the user; a blank value is stored as `NULL` ("uncategorized"). There is no predefined list, no enum, no check constraint, and no lookup table; Shared exposes `Category` as `string?` on the note DTOs. Letter case is kept as entered. Notes can be created, listed (paged, newest first), edited (body and category), and deleted. Body is plain text, 1-4,000 characters (trimmed), rendered with Blazor's default HTML encoding only (never `MarkupString`); the category is rendered the same way (as a badge with encoded text). Category values must not contain control characters.
- **Rationale**: The user wants to decide their own categories (spec FR-009c). A plain nullable column is the simplest model that satisfies this; it also avoids a migration whenever someone wants a new category.
- **Alternatives**: Fixed enum with check constraint (rejected by the requirement); a per-user categories table with autocomplete (more endpoints and tests than v1 needs; a distinct-values endpoint for suggestions can be added later without a schema change).

## R9. Images in Azure Blob Storage

- **Decision**: Image files live in a private blob container `vehicle-images` in the storage account (Azurite locally). Metadata lives in PostgreSQL `vehicle_images(id, vehicle_id, content_type, size_bytes, caption, is_main, created_at, created_by)`; there is no content column. The blob name is derived, not stored: `{vehicleId}/{imageId}`. Rules: JPEG/PNG/WebP only; max 5 MB; max 10 per vehicle; caption optional, max 200 characters. The Api verifies the file by **signature** (JPEG `FF D8 FF`, PNG `89 50 4E 47 0D 0A 1A 0A`, WebP `RIFF....WEBP`) and stores the detected content type as blob and row content type, ignoring the client's file name and content type; the original file name is not stored.
  - **Access**: the API reads and writes blobs with `BlobServiceClient`. Locally it uses the emulator connection provided by Aspire; in Azure it uses `DefaultAzureCredential` (the API's managed identity with *Storage Blob Data Contributor* on the account; shared-key access and anonymous blob access disabled; HTTPS only). The container is created at API startup if absent (infrastructure may pre-create it). No SAS tokens or storage URLs are ever returned to clients.
  - **Consistency (blob + row)**: *Add*: in one PostgreSQL transaction lock the owner-scoped vehicle row, enforce the 10-image limit, insert the row (first image is main), upload the blob, then commit; if the upload fails the transaction rolls back, and if the commit fails the just-written blob is deleted (best effort). *Delete image*: delete the row (promote the oldest remaining image to main if needed) and commit, then delete the blob. *Delete vehicle*: delete the vehicle (rows cascade) and commit, then delete all blobs under the prefix `{vehicleId}/`. A failed post-commit blob delete leaves a private orphan, which is logged (ids only) and is harmless; a periodic orphan sweep is a recorded future enhancement.
  - **Read**: the repository finds the metadata row with the owner-scoped SQL (404 if absent), then streams the blob. If the row exists but the blob is missing or storage is unavailable, the Api returns `503`/`404` problem details and the Web shows the placeholder; vehicle details, tags, and notes are unaffected (spec edge case). Main-image flag changes affect only PostgreSQL.
  - **Serving**: `GET .../images/{imageId}/content` streams with the stored content type, `X-Content-Type-Options: nosniff`, and `Cache-Control: private, max-age=86400` (image ids are immutable).
- **Rationale**: The user asked for a storage account. It keeps binary data out of the relational store, matches how the app will run in Azure, and the same API code runs against Azurite locally and in tests. Signature sniffing avoids an imaging-library dependency and defends against disguised files. No decoding or thumbnailing in v1; cards scale the stored picture with CSS and `loading="lazy"`.
- **Risks**: Large photos on card pages increase bandwidth (mitigated by 5 MB cap, lazy loading, private caching; thumbnails are a recorded future enhancement); no cross-store transaction (mitigated by the ordering above); Azurite is a development emulator only.
- **Alternatives**: `bytea` in PostgreSQL (simpler transactions, but contradicts the requirement and bloats the database); SAS URLs handed to the browser (violates private-by-default and spreads authorization outside the API).

## R10. Upload transport

- **Decision**: The Api upload endpoint is `POST /api/vehicles/{id}/images` as `multipart/form-data` (`file`, optional `caption`), bound with `[FromForm]`, with `.DisableAntiforgery()` (bearer-token API, no cookies) and a request size limit slightly above 5 MB. The Web app receives the browser upload on `POST /vehicles/{id}/images` (antiforgery-protected minimal endpoint, `IFormFile`, request size limit), forwards the stream to the Api with the user's token, then redirects back to the vehicle page with a status message. Content is streamed, not buffered into strings.
- **Rationale**: A plain HTML form post works in static SSR without interactive file handling, keeps the token on the server, and gives deterministic size limiting at both hops. FluentValidation validates the upload request (caption length, file present, size); the signature check returns a validation problem on the `file` field. The Api streams the validated upload to Blob Storage (R9) rather than buffering it in memory beyond the signature check.
- **Alternatives**: `InputFile` in an interactive circuit (needs a circuit-safe token provider, see R11); direct browser-to-API upload (would expose the bearer token to the browser).

## R11. Web rendering approach

- **Decision**: Vehicle pages (`/vehicles`, `/vehicles/new`, `/vehicles/{id}`, `/vehicles/{id}/edit`) are static SSR with `[ExcludeFromInteractiveRouting]` and `[Authorize]`, following feature 001 R4. Mutations (create, edit, delete, add/remove tag, add/edit/delete note, set main image, delete image) are named `EditForm` posts handled by the page, then redirect (post-redirect-get). Paging is link-based (`?page=n`, `?notesPage=n`) so it works without JavaScript. Bootstrap provides cards (`row-cols-*` grid, `card`, `card-img-top` with a placeholder), badges for tags and categories, pagination, and confirm dialogs for deletes (a Bootstrap modal via data attributes; no custom JavaScript).
- **Rationale**: Reuses the existing token-forwarding handler without touching circuit/token lifetime. The app stays Interactive Server at the app level; the deviation is limited to these pages and recorded in Complexity Tracking.
- **Alternatives**: Interactive Server pages with a circuit-scoped token cache (more security surface, not required).

## R12. Image display in the browser

- **Decision**: Cards and galleries use `<img src="/vehicles/{vehicleId}/images/{imageId}">`. This Web endpoint requires the signed-in cookie, calls the Api with the user's token, and streams bytes with the same headers as R9. `alt` text is the vehicle nickname or the caption. Placeholder is an inline Bootstrap-styled element, not an asset fetch.
- **Rationale**: Browsers cannot attach bearer tokens to `<img>`; the proxy keeps images private to the owner and the token server-side.
- **Alternatives**: Base64 data URIs in HTML (bloats pages, defeats caching); public/signed URLs (violates private-by-default).

## R13. Validation limits (consolidated)

| Field | Rule |
|---|---|
| `nickname` | required, 1-100 chars (trimmed) |
| `make`, `model` | required, 1-50 chars |
| `trim` | optional, <= 50 chars |
| `color` | optional, <= 30 chars |
| `mileage` | optional, integer 0-9,999,999 |
| `doors` | optional, integer 1-6 |
| tag | 1-30 chars, <= 20 per vehicle, unique ignoring case |
| note body | 1-4,000 chars; category in the fixed list |
| image | JPEG/PNG/WebP by signature, <= 5 MB, <= 10 per vehicle, caption <= 200 |
| paging | `page >= 1`; `pageSize` 1-50 |
| note category | optional; trimmed; <= 40 chars; blank -> none; no control characters |
| search `q` | optional; trimmed; <= 100 chars; blank -> no search |
| filters | `make` <= 50, `color` <= 30, `tag` <= 30, `doors` 1-6, `minMileage`/`maxMileage` 0-9,999,999 with `minMileage <= maxMileage` |

Control characters are rejected in all single-line fields. Values are stored as entered (trimmed) and always output-encoded.

## R14. Observability and logging

- **Decision**: Npgsql and Azure SDK tracing are enabled by the Aspire client integrations; Dapper and blob calls run inside those traces. Log messages contain vehicle/note/tag/image ids, counts, and outcomes only; never nicknames, search text, filter values, tag text, note text/categories, or image bytes/names (FR-018). A small test asserts representative flows do not write field values to a captured logger.
- **Rationale**: Constitution observability and data-protection rules.

## R15. Test strategy

- **Database.Tests**: shared `PostgresFixture` and `AzuriteFixture` (Testcontainers) per test collection. Migration tests: applies cleanly to an empty database, is idempotent on rerun, detects a tampered checksum, concurrent runners do not conflict. Repository tests per feature: CRUD, owner scoping (a second owner never sees or changes rows), paging order and out-of-range handling, search/filter matrix (R16), tag uniqueness/limit, note ordering and free-text/blank categories, image invariants (main flag, limit, blob written and removed, blob cleanup on vehicle delete, rollback when the blob upload fails).
- **Api.Tests**: `WebApplicationFactory<Program>` with Testcontainers connection strings and the existing `TestJwt`. For every endpoint: success, validation failure, not-found, 401 unauthenticated, and cross-user isolation (user B gets 404 for user A's ids and sees none of A's vehicles in list/search/filter-options). Validator unit tests; mapper tests (data record <-> DTO); image signature tests (valid, spoofed content type, truncated, oversize).
- **Web.Tests**: bUnit for `VehicleCard`, `VehicleFilters` (active criteria, clear, no-match vs empty states, query-string round trip), pager (first/middle/last, clamped, keeps filters), forms showing validation messages; `WebApplicationFactory` for the image proxy (requires sign-in, forwards token, headers) and upload endpoint (antiforgery, size limit).
- **AppHost.Tests**: smoke test additionally waits for the `postgres`/`garagedb` and `storage`/`blobs` resources to be healthy.

## R16. Search and filtering of the garage

- **Decision**: `GET /api/vehicles` accepts optional `q`, `make`, `color`, `doors`, `tag`, `minMileage`, `maxMileage` in addition to `page` and `pageSize`. One SQL file (`Scripts/Vehicles/ListPage.sql`) handles all combinations with optional-parameter predicates (`@Make IS NULL OR lower(v.make) = lower(@Make)` and so on), always with `v.owner_id = @OwnerId`.
  - **Search `q`**: case-insensitive *contains* over `nickname`, `make`, `model`, `trim`, `color`, and tag names (via `EXISTS` on `vehicle_tags`). The repository builds the pattern in C# (`LikePattern.Contains`) by escaping `\`, `%`, and `_` and wrapping with `%`, then passes it as a parameter used with `ILIKE @Pattern ESCAPE '\'`; user text is therefore matched literally and is never concatenated into SQL. Whitespace-only `q` is treated as absent.
  - **Filters**: `make` and `color` are case-insensitive equality; `doors` equality; `tag` equality on the tag's lower-cased key (a vehicle must carry that tag); `minMileage`/`maxMileage` inclusive bounds (vehicles with no mileage never match a mileage bound). All criteria are ANDed with `q`.
  - **Filter options**: `GET /api/vehicles/filter-options` returns the distinct makes, colors, and tag names present in the caller's garage (sorted, case-insensitively de-duplicated), also owner-scoped, used to populate the filter dropdowns (FR-025). Doors uses the fixed 1-6 range.
  - **Counts/paging**: the filtered total drives `TotalCount`/`TotalPages`; out-of-range pages clamp as in R6.
  - **Web**: the garage page has a GET form (so URLs are bookmarkable and no antiforgery token is needed) with a search box and a collapsible filter panel (Bootstrap collapse via data attributes, open when any filter is active); active criteria are shown as removable badges with a "Clear all" link; pager links preserve all criteria. The page distinguishes "no vehicles yet" (empty garage, zero criteria) from "no vehicles match" (criteria present, zero results). Invalid criteria (for example min > max) show a validation message and keep the page rendering.
- **Rationale**: Garages are tens of vehicles, so `ILIKE` over an owner-scoped set (index on `owner_id`) comfortably meets SC-009 without full-text indexes or extensions. Keeping everything in one parameterized statement keeps results and counts consistent and owner-scoped.
- **Alternatives**: PostgreSQL full-text search or `pg_trgm` (new indexes/extensions, unnecessary at this scale and harder to match substrings in nicknames); client-side filtering in the Web (would break paging and ship the whole garage); dynamic SQL composition (prohibited by parameterization rules and harder to test).
