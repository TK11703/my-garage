# Web Routes and UI Contracts: Window Sticker Upload Shortcut

Changes in `MyGarage.Web`. All routes already require sign-in (existing fallback policy). Pages follow [docs/ui-standard.md](../../../docs/ui-standard.md): Bootstrap 5.3 only, accessible names on icon-only controls, `ApiFailureAlert` for API failures, user-supplied and extracted text rendered escaped (no `MarkupString`).

## Routes

No new routes. Both existing pages gain a second named form:

| Page | Existing form | New form | Posts to |
|---|---|---|---|
| `/vehicles/new` | `create-vehicle` | `import-sticker` | the same page |
| `/vehicles/{vehicleId:guid}/edit` | `update-vehicle` | `import-sticker` | the same page |

The import post is `multipart/form-data` with an antiforgery token. Fields: `stickerFile` (the file). Nothing about the vehicle is posted by this form; the edit page loads the stored vehicle from the API as it does for every request.

## Tabs

| Form | Tabs, in order | Opens on |
|---|---|---|
| Add vehicle | Import from sticker, Details | Import from sticker; after an import or a rejected save, Details |
| Edit vehicle | Import from sticker, Details, Media, Description | Details (unchanged); `?tab=import` opens Import from sticker; an unknown `tab` value means Details |

- Tab id `import`, label "Import from sticker", an upload icon. The detail page's tabs (`VehicleTabs.All`) are unchanged.
- A rejected vehicle save still opens the tab with the first error (Details); an import that ends with an error stays on Import from sticker so the message is next to the file chooser.

## Import from sticker pane

1. **Heading and help**: "Import from sticker". Text: "Upload the window sticker to fill in the vehicle details. PDF, JPG, PNG, or WebP, up to 10 MB. Nothing is saved until you save the vehicle, and the sticker file is not kept."
2. **Warning (edit and add)**: "Importing replaces any unsaved changes on this form with the saved vehicle plus the values found on the sticker." On the add form the text reads "...with the values found on the sticker."
3. **File chooser**: labelled file input (`name="stickerFile"`, `accept="application/pdf,image/jpeg,image/png,image/webp"`), described by the help text; the drop-area styling reuses `mg-dropzone` and `dropzone.js` behavior for dropping one file into the input.
4. **Chosen file** (script enhancement, `sticker-preview.js`, `aria-live="polite"` region): an image preview for images, an embedded `<object type="application/pdf">` with a fallback link for PDFs, otherwise the name and size; a "Remove file" button clears the input. Object URLs are revoked on change and on `pagehide`. A file over 10 MB or of an unsupported type is flagged immediately and the button stays disabled (the server repeats these checks).
5. **Accept import**: submit button, disabled until a file is chosen; on submit it shows a busy state ("Reading the sticker...", `aria-busy`) and disables itself via the existing `submit-busy.js`, so the form cannot be submitted twice. A short text under it: "This may take up to two minutes."
6. **Result summary** (after an import, `role="status"`): file outcome in plain words:
   - Filled / Partial: "Read N details from the sticker. Review them on the Details tab." with notices listed (see below) and a button to open Details.
   - No details found: "No vehicle details were found in that file. Try a clearer, upright photo or a PDF, or enter the details by hand." The form is unchanged.
   - Error (see Failures): the specific message and the form unchanged.
7. **Notices** (from `StickerImportResult.Notices`): unreadable fields ("These values could not be used and were left empty: VIN, Horsepower."), multiple vehicles ("The file seems to show more than one vehicle; only one was read."), duplicate VIN ("A vehicle with this VIN is already in your garage."; adding is still allowed, and no link is given because the VIN must never be put in an address), features left out ("N features were left out to stay within 100 per vehicle.").

## Details tab changes

- After an import the Details tab shows the filled values in their normal fields and tabs (Basic Info and Specifications groups). Every imported value is an ordinary, editable value; there is no special styling that can be mistaken for validation.
- **Unmatched lookups**: when the sticker's vehicle type, fuel type, transmission, or drivetrain matched none of the user's shown items, the dropdown stays unset and a `form-text` hint under it reads "The sticker says: {text}" (escaped), so the user can pick an item or add one in Preferences. Nothing is added to the lists automatically.
- **Features from the sticker** (a new group below Specifications, shown only when there are proposals): a list of rows, each with a checkbox (checked by default, label = feature name) and, when it replaces existing features, "Replaces: {names}". Hidden inputs carry the name and the replaced tag ids. Introductory text: "These features will be added to the vehicle when you save. Features it replaces will be removed. Untick any you do not want." Rows re-render from the posted values after a rejected save.

## Save behavior

- The vehicle form's existing submit sends `ToRequest()`, which now includes `StickerFeatures` built from the ticked rows. After a successful create or update the page navigates to the vehicle's detail page as today; the detail page's Features list shows the result.
- A 400 problem on `StickerFeatures` (for example the 100-feature limit) opens Details and shows the message in the Features-from-the-sticker group.

## Failures and messages

| API result | Message shown in the Import pane | Form |
|---|---|---|
| No file or an empty file | "Choose a file first." | unchanged |
| 400 (unsupported or damaged) | "That file is not a valid PDF, JPG, PNG, or WebP." | unchanged |
| 413 or over 10 MB | "That file is larger than 10 MB." | unchanged |
| 404 on an edit | The standard vehicle-not-found page | n/a |
| 429 | "You have started several imports in a row. Wait a minute and try again." | unchanged |
| 503 `not_configured` | "Reading stickers is not set up here. Enter the details by hand." | unchanged |
| 503 `timeout` / `rate_limited` / `service_error` / `invalid_output` / `content_filter` | "The sticker could not be read right now. Nothing was changed; try again or enter the details by hand." | unchanged |
| 401 | The standard sign-in prompt (`ApiFailureAlert`, return to the same page) | unchanged |
| Network failure | `ApiFailureAlert` "unavailable" | unchanged |

User-controlled text (file name, extracted values) is never inserted into messages other than as escaped plain text in the hints above.

## Client behavior and limits

- `StickerImportApiClient` is a typed `HttpClient` against the API with a 105 s total timeout, no automatic retry, and the signed-in user's token; it streams the uploaded file without buffering it in memory.
- The page checks size and emptiness before calling the API, and never logs the file name or any returned value.
- If a Content-Security-Policy is ever added to Web pages, the preview needs `img-src blob:` and `object-src blob:` (or `frame-src blob:`); the private-file proxy's policies are unaffected because the sticker is never served back.

## Accessibility

- The file input has a visible label and help text via `aria-describedby`; the preview region and result summary are polite live regions.
- Tab order: tab strip, file chooser, Remove file, Accept import; focus moves to the result summary heading after the page reloads from an import.
- The feature rows are a labelled group (`role="group"`, `aria-labelledby`); each checkbox has the feature name as its label.
- `prefers-reduced-motion` is respected (no animation beyond Bootstrap's defaults).

## Amendment 3: Description tab features

- `GET /vehicles/{id}?tab=description&featureQuery=text`: the Features card shows a search box (`role="search"`, a labelled `featureQuery` input, at most 30 characters) and one card per category (heading, count badge, first seven features, `<details>` "Show N more features" for the rest). With a search, only matching features are listed, all of them, with "N features match "text"." and a "Clear search" link, or "No features match "text"." when none do.
- On large screens the features section takes 8 of 12 columns and the journal section the other 4 (full width below that); feature text is small.
- Edit form (`VehicleTagsSection`): each feature shows its category as a badge next to its name.
- API: `VehicleTagDto` (in the vehicle detail and the add-feature response) has a `category` string.
