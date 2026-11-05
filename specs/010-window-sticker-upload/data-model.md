# Data Model: Window Sticker Upload Shortcut

No tables, columns, or migrations are added. The feature introduces transient contract types (they exist only in one request/response and in the rendered form) and one new field on the existing vehicle save request. Persistence reuses `vehicles`, `features`, and `vehicle_tags` (see feature 004).

## Transient contract types (`MyGarage.Shared/Vehicles/StickerImport`)

### StickerImportResultDto (response of the extraction call)

| Field | Type | Notes |
|---|---|---|
| `Outcome` | `StickerImportOutcome` | `Filled` (every found value usable), `Partial` (some values unreadable, dropped, or left out), `NoDetailsFound` (a readable file with no vehicle details; nothing to fill). |
| `Year` | `int?` | Same bounds as `VehicleRequest.Year`. |
| `Make`, `Model`, `Trim` | `string?` | Trimmed; lengths per the vehicle validator (50). |
| `Vin` | `string?` | Upper-cased; 17 characters, no I, O, or Q. |
| `VehicleTypeId`, `FuelTypeId`, `TransmissionId`, `DrivetrainId` | `Guid?` | Id of the user's **shown** lookup item whose name equals the sticker text ignoring case and spacing; otherwise null. |
| `VehicleTypeText`, `FuelTypeText`, `TransmissionText`, `DrivetrainText` | `string?` | The sticker's text when no item matched (shown as a hint next to the empty field); null when matched or not printed. |
| `Engine` | `string?` | Up to 100 characters. |
| `Horsepower`, `Seats`, `Doors` | `int?` | Same bounds as the vehicle form. |
| `Color` | `string?` | Exterior color, up to 30 characters. |
| `WheelSize` | `decimal?` | Inches, one decimal place, same range as the form. |
| `TireSize` | `string?` | Up to 20 characters. |
| `Features` | `StickerFeatureDto[]` | Proposed features (see below), at most as many as fit under the 100-per-vehicle limit. |
| `Notices` | `StickerNoticeDto[]` | Stable codes with a short message each: `NoDetails`, `PartiallyRead`, `UnreadableValues` (names of fields whose values were dropped), `FeaturesLeftOut` (count), `MultipleVehicles`, `DuplicateVin`, `NotAStickerLikely`. No notice contains a VIN or any extracted text other than field names. |
| `FieldsFilled` | `int` | Count of the vehicle fields given a value (lookup text hints do not count). |

**Validation (server side, before returning)**: every extracted value is placed in a `VehicleRequest` and checked with `VehicleRequestValidator`; each value with an error is removed and its field name added to the `UnreadableValues` notice. Required-field errors (missing year, make, or model) are ignored here; they apply when the user saves.

### StickerFeatureDto

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | 1 to 30 characters, trimmed, unique among the proposals ignoring case. |
| `ReplacesTagIds` | `Guid[]` | Ids of the edited vehicle's existing `vehicle_tags` rows this feature would replace; empty on an add and when nothing is a duplicate. Always a subset of the vehicle's own tags. |
| `ReplacesNames` | `string[]` | The names for display, in the same order. |

### Input to the extraction call (not a stored type)

- `file` (multipart): PDF, JPEG, PNG, or WebP, at most `StickerImportLimits.MaxFileBytes` (10 MB), identified by signature.
- `vehicleId` (optional form value): the edited vehicle; must belong to the caller or the call answers 404.

## New field on an existing contract

### VehicleRequest.StickerFeatures

| Field | Type | Notes |
|---|---|---|
| `StickerFeatures` | `StickerFeatureRequest[]?` | Optional, on create and update. `null` or empty means no feature changes (existing behavior). |

`StickerFeatureRequest`: `Name` (1 to 30 characters, no control characters, trimmed) and `ReplacesTagIds` (`Guid[]?`).

**Validation** (`StickerFeatureRequestValidator`, run inside `VehicleRequestValidator`):

- At most 100 entries; names unique ignoring case.
- On a create, `ReplacesTagIds` MUST be empty (a new vehicle has no features).
- On an update, replaced ids that are not tags of this vehicle are ignored (they may have been removed meanwhile); they are never applied to another vehicle because the delete is scoped by vehicle id and owner in SQL.
- After applying, the vehicle MUST have at most 100 features; otherwise the save is rejected with a 400 problem on `StickerFeatures` and nothing is changed.

## Persistence effects (existing tables)

On create or update with `StickerFeatures`, inside the transaction that writes the vehicle:

1. `vehicle_tags`: delete the rows in `ReplacesTagIds` that belong to this vehicle. Duplicates by feature key are already included in `ReplacesTagIds` when the proposal is built (the API computes the key), so the database applies only the ids it is given.
2. `features`: upsert each sticker feature by `lower(name)` (an existing shared feature keeps its spelling).
3. `vehicle_tags`: insert the link for each sticker feature unless this vehicle already has it.

No other table changes. Feature rows left with no vehicle are untouched (the suggestion query already lists only features in use).

## Form model (Web, `VehicleFormModel`)

- `StickerFeatures`: `List<StickerFeatureChoice>` (getter `field ??= []`). `StickerFeatureChoice`: `Name`, `Include` (default true), `ReplacesTagIds` (list), `ReplacesNames` (list).
- `ToRequest()` sends only the rows with `Include == true`.
- Not posted and not persisted: the unmatched lookup texts and notices; they are rendered from the import response during the request that handled the import only.

## State and lifecycle

```text
[Import tab: no file] --choose file--> [file shown (client-side only)] --Accept import--> [request: Web -> API -> model]
   --> Filled / Partial: Details tab opens with values, proposals, hints, and notices   (nothing stored)
   --> NoDetailsFound or error: form unchanged, message shown                            (nothing stored)
[Details tab with values] --Save--> vehicle written + sticker features applied in one transaction --> redirect to the vehicle
[Details tab] --leave without saving--> nothing exists (no file, no result, no change)
```

## Relationships and ownership

- The edited vehicle's features are read only through owner-scoped SQL (`vehicles.owner_id = @OwnerId`); a vehicle that is not the caller's is "not found".
- The lookup items used for matching are the caller's own (shown items only, as the dropdowns offer).
- No cross-user data is read or returned; shared feature names are only written through the existing upsert and are visible to others only as already defined by feature 004.

## Amendment 3: feature category

- **`features.category`** (`varchar(40)`, nullable): one of `FeatureCategories.All`. Null until categorized; set once by `Tags/SetCategory.sql`. A partial index `ix_features_uncategorized` serves the pending list (`Tags/ListUncategorized.sql`).
- **`VehicleTagDto.Category`** (`string`): the feature's category, or Other while it has none. `VehicleTagRecord.Category` is the nullable stored value.
- Because a feature is shared by name, every vehicle that has it shows the same category.
