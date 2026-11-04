---

description: "Task list for Discounts, Labor Totals, and Tax on Service Records"
---

# Tasks: Discounts, Labor Totals, and Tax on Service Records

**Input**: Design documents from `/specs/009-invoice-pricing-rules/`

**Prerequisites**: plan.md, spec.md, research.md (R1-R8), data-model.md, contracts/pricing-api.md, quickstart.md

**Tests**: Required (constitution VIII): write the test, run it, watch it fail for the right reason, then implement. The database is never mocked; only the AI boundary is faked.

**Organization**: Grouped by user story. US1 discounts (P1), US2 labor total (P1), US3 tax (P2), US4 emailed invoices fill these fields (P2). The Foundational phase holds the database layer and the shared DTO fields that all four stories read, so the stories can then be done in any order, but US4 needs US1-US3.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1-US4, only on user-story phase tasks
- Field names (keep identical everywhere): line item `FlatCost`, `LaborHours`, `LaborRate`, `LaborCost` (the new entered or effective labor total); record `Tax` (request, DTO, proposal) and `tax_amount` (column); line column `labor_cost`; migration `0013_pricing_discounts_labor_tax`
- No new NuGet package; no new project; no new endpoint
- Never run `dotnet test` while `aspire run` is active
- Work on branch `009-invoice-pricing-rules`; never commit to `main`; merge by pull request

## Phase 1: Setup

- [ ] T001 On branch `009-invoice-pricing-rules`, run `dotnet build MyGarage.slnx` (zero warnings) and `dotnet test tests/MyGarage.Web.Tests` to record the baseline; Docker must be running for the other suites

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The migration, the calculator, the database records and SQL, and the Shared DTO fields. Nothing else compiles against the new fields until these exist.

- [ ] T002 [P] Write failing tests in `tests/MyGarage.Database.Tests/Common/MigrationRunnerTests.cs`: the ledger lists `0013_pricing_discounts_labor_tax` last; the migration accepts a line item with `flat_cost` -4.04 and `total_cost` -4.04 and rejects -10000000; adds `service_records.tax_amount` (nullable, rejects -0.01 and 10000000); adds `service_line_items.labor_cost` (nullable, rejects -0.01); rejects `labor_cost` with `labor_hours`; rejects `flat_cost` with `labor_cost`; and rows written before the migration (insert them through a script run against a database migrated only to `0012`, if the fixture allows it, otherwise assert the new columns read null on an inserted legacy-shaped row) keep their values. Also update the expected script list in `tests/MyGarage.Database.Tests/Common/SqlScriptStoreTests.cs`
- [ ] T003 Create `src/MyGarage.Database/Scripts/Migrations/0013_pricing_discounts_labor_tax.sql` per data-model.md: look up and drop the two unnamed CHECKs on `flat_cost` and `total_cost` by `pg_constraint` (inside a `DO` block) and recreate them with the symmetric range under explicit names; add `labor_cost` and `tax_amount` with their CHECKs; replace `ck_service_line_items_flat_or_breakdown`; add `ck_service_line_items_labor_cost_without_hours`. Run T002 and confirm it passes
- [ ] T004 [P] Write failing tests in `tests/MyGarage.Database.Tests/ServiceRecords/ServiceCostCalculatorTests.cs`: a flat cost of -4.04 gives a line total of -4.04; labor cost alone (no hours or rate, no parts) gives that amount; labor cost plus parts sums; hours and rate give the calculated amount and ignore a supplied labor cost; rounding is half away from zero for negative values too
- [ ] T005 Update `src/MyGarage.Database/ServiceRecords/ServiceCostCalculator.cs`: add an `EffectiveLaborCost(hours, rate, laborCost)` helper and make `LineTotal` take the labor cost (flat cost wins; otherwise effective labor cost plus parts). Run T004
- [ ] T006 [P] Write failing repository tests in `tests/MyGarage.Database.Tests/ServiceRecords/ServiceLineItemRepositoryTests.cs` and `ServiceRecordRepositoryTests.cs` (helpers in `ServiceRecordTestData.cs`): a discount line is stored and read back with a negative total; a labor-cost-only line is stored with `labor_cost` and no hours or rate; hours and rate store no `labor_cost` even when one is supplied; a record created with a tax reads it back; the record total in `GetById` and `ListPage` is the line totals plus the tax, and is negative when discounts exceed charges; a record with no tax totals as before; updating the tax and clearing it (null) works; every query stays scoped to the owner (another owner reads and writes nothing)
- [ ] T007 Update the database layer to make T006 pass: `ServiceLineItemInput.cs` and `ServiceLineItemRecord.cs` gain `LaborCost`; `ServiceRecordRecord.cs` gains `Tax`; `ServiceRecordRepository.cs` accepts and passes the tax on add and update and passes the labor cost (null when hours and rate are present) and the recomputed line total on line add and update; `Scripts/ServiceRecords/Insert.sql`, `Update.sql`, `InsertLineItem.sql`, `UpdateLineItem.sql` take the new parameters; `GetById.sql` and `ListPage.sql` select `tax_amount` and add `COALESCE(sr.tax_amount, 0)` to `total_cost`; `ListLineItems.sql` selects `labor_cost`
- [ ] T008 [P] Write failing tests in `tests/MyGarage.Database.Tests/Drafts/DraftAcceptanceRepositoryTests.cs`: accepting a service record draft stores its tax and each line's labor cost and negative flat cost, and the duplicate lookup (`FindDuplicateRecord`) compares against line totals plus tax
- [ ] T009 Update `src/MyGarage.Database/Drafts/AcceptedRecordValues.cs` (service record values gain `Tax`), `DraftAcceptanceRepository.cs` (pass the tax and labor cost), and `Scripts/Drafts/FindDuplicateRecord.sql` (add `COALESCE(sr.tax_amount, 0)` to the compared total). Run T008
- [ ] T010 [P] Add the Shared DTO fields (no behavior): `src/MyGarage.Shared/ServiceRecords/ServiceRecordRequest.cs` and `ServiceRecordDto.cs` (and the summary DTO if it lists totals separately) gain `Tax`; `ServiceLineItemRequest.cs` gains `LaborCost`; `ServiceLineItemDto.cs` keeps `LaborCost` (now the effective labor total); `src/MyGarage.Shared/Drafts/DraftProposal.cs` gains `Tax` on `ServiceRecordProposal` and `LaborCost` on its line items. Update the comment on `ServiceLineItemRequest` to describe the new rule

**Checkpoint**: Database and Shared compile and their tests pass; the API and Web behavior is unchanged until the stories below.

---

## Phase 3: User Story 1 - Record a discount (Priority: P1)

**Goal**: A flat cost below zero is accepted, totals follow, and the UI shows a credit.

**Independent Test**: Quickstart local step 3, first bullet; a flat cost of -10,000,000.00 is rejected.

- [ ] T011 [P] [US1] Write failing validator tests in `tests/MyGarage.Api.Tests/ServiceRecords/ServiceLineItemRequestValidatorTests.cs`: -4.04 accepted; -9,999,999.99 and 9,999,999.99 accepted; -10,000,000.00 rejected with the new range message; three decimals rejected; a negative flat cost with labor hours or a part on the same line is rejected with the existing pricing message
- [ ] T012 [US1] Update `src/MyGarage.Api/ServiceRecords/ServiceLineItemRequestValidator.cs`: flat cost range -`MoneyRules.MaxAmount` to `MoneyRules.MaxAmount` with the message "Flat cost must be between -9,999,999.99 and 9,999,999.99."; keep the exclusivity rule. Run T011
- [ ] T013 [P] [US1] Write failing endpoint tests in `tests/MyGarage.Api.Tests/ServiceRecords/ServiceLineItemsEndpointsTests.cs` and `ServiceRecordsEndpointsTests.cs`: create a record, add a 50.00 flat line and a -4.04 flat line, and the response and the list show a record total of 45.96 and the line total -4.04; success, validation failure, and not-found cases stay covered; another user's record still returns not found
- [ ] T014 [P] [US1] Write failing Web tests: `tests/MyGarage.Web.Tests/ServiceRecords/ServiceLineItemFormTests.cs` (the flat cost input has no minimum of 0 and accepts -4.04), `ServiceRecordLineItemsViewTests.cs` and `ServiceRecordDetailPageTests.cs` (a negative line and a negative record total render with a leading minus and a visually hidden "credit" label, and a positive amount renders as before)
- [ ] T015 [US1] Update `src/MyGarage.Web/ServiceRecords/ServiceLineItemForm.razor` (flat cost `min="-9999999.99"`), `ServiceLineItemRow.razor`, `ServiceRecordFormat.cs`, and the record detail and list components so a negative amount renders with the minus sign and a visually hidden "credit" label; keep the money formatter in `src/MyGarage.Web/Common/DisplayFormat.cs` as the single place that formats amounts. Run T013 and T014

**Checkpoint**: A discount can be entered, saved, listed, and totaled end to end.

---

## Phase 4: User Story 2 - Labor with only a total (Priority: P1)

**Goal**: "Total labor" follows Hourly rate on a line; calculated from hours and rate, or typed when both are empty.

**Independent Test**: Quickstart local step 3, labor bullets.

- [ ] T016 [P] [US2] Write failing validator tests in `ServiceLineItemRequestValidatorTests.cs`: labor cost alone is accepted; labor cost with parts is accepted; hours and rate with a labor cost are accepted and the labor cost is ignored by the later steps; hours without a rate and a rate without hours are rejected with the existing messages; a labor cost with a flat cost is rejected; labor cost -0.01, 10,000,000.00, and three decimals are rejected; a line with none of flat cost, labor, labor cost, or parts is rejected with the pricing message; the total-over-maximum rule counts the labor cost
- [ ] T017 [US2] Update `ServiceLineItemRequestValidator.cs`: add the `LaborCost` range and decimals rules, include `LaborCost` in `hasBreakdown`, and in the over-maximum check; update `src/MyGarage.Api/ServiceRecords/ServiceRecordMapper.cs` (`ToInput` passes `LaborCost` only when hours and rate are both null; `ToLineItemDto` returns the effective labor cost). Run T016 and the mapper tests in `ServiceRecordMapperTests.cs` (add cases: stored labor cost shown; calculated wins; none gives null)
- [ ] T018 [P] [US2] Write failing endpoint tests in `ServiceLineItemsEndpointsTests.cs`: a labor-cost-only line returns `laborCost` 180.00 and `totalCost` 180.00; with a 25.00 part the total is 205.00; sending hours, rate, and a different `laborCost` returns the calculated labor cost; sending hours without a rate returns 400 with the message
- [ ] T019 [P] [US2] Write failing Web tests: `ServiceLineItemFormTests.cs` (a "Total labor" input follows "Hourly rate"; it is disabled and shows the calculated amount when both hours and rate have values; enabled and editable when both are empty; clearing hours and rate re-enables it; hours alone shows the existing error) and `ServiceRecordLineItemsViewTests.cs` (a labor-cost-only line shows "Labor: $180.00" and no hours or rate text)
- [ ] T020 [US2] Update `src/MyGarage.Web/ServiceRecords/ServiceLineItemFormModel.cs` (`LaborCost`, `From`, `ToRequest`, the pricing-mode detection so a labor-cost-only line is a breakdown line), `ServiceLineItemForm.razor` (the "Total labor" input after "Hourly rate" with `aria-describedby` and a field error, disabled and calculated when both are present), `ServiceLineItemRow.razor`, and `ServiceRecordFormat.cs` (labor line text for a total-only labor). Run T018 and T019

**Checkpoint**: Labor can be recorded either way, on a line with or without parts.

---

## Phase 5: User Story 3 - Tax on a record (Priority: P2)

**Goal**: An optional tax on the record, shown separately, included in the total.

**Independent Test**: Quickstart local step 3, tax bullet; a negative tax is rejected.

- [ ] T021 [P] [US3] Write failing validator tests in `tests/MyGarage.Api.Tests/ServiceRecords/ServiceRecordRequestValidatorTests.cs`: tax null, 0.00, 8.25, and 9,999,999.99 accepted; -0.01, 10,000,000.00, and 8.255 rejected with messages that name the range or the decimal rule
- [ ] T022 [US3] Update `ServiceRecordRequestValidator.cs` (tax rules) and `ServiceRecordMapper.cs` (request to repository input; `Tax` on `ServiceRecordDto`; `ServiceRecordSummaryDto` total unchanged because the total now includes the tax). Run T021 and the mapper tests
- [ ] T023 [P] [US3] Write failing endpoint tests in `ServiceRecordsEndpointsTests.cs` and `ServiceRecordsChangeEndpointsTests.cs`: create with tax 8.25 returns `tax` 8.25 and `totalCost` equal to lines plus 8.25; update clears the tax; a negative tax returns 400 `ProblemDetails` naming `Tax`; the list shows the total with tax; `ServiceRecordVehicleScopeTests.cs` keeps another owner from reading or changing the tax
- [ ] T024 [P] [US3] Write failing Web tests: `ServiceRecordCreatePageTests.cs` and `ServiceRecordEditPageTests.cs` (a "Tax" input after the shop field; saved value round-trips; empty means none), `ServiceRecordDetailPageTests.cs` (a tax line shown separately from the line items and the total includes it; no tax line when none; a pre-existing record shows no tax)
- [ ] T025 [US3] Update `ServiceRecordFormModel.cs`, `ServiceRecordForm.razor` (Tax input with `min="0"`, `max="9999999.99"`, `step="0.01"`, label, field error), `ServiceRecordDetail.razor`, `ServiceRecordsSection.razor` if it shows totals, and `ServiceRecordFormat.cs`. Run T023 and T024

**Checkpoint**: Tax is entered, stored, shown, and totaled for a manually entered record.

---

## Phase 6: User Story 4 - Emailed invoices fill these fields (Priority: P2)

**Goal**: A draft built from an invoice puts discounts, labor totals, and tax in the right places, and its check agrees with the invoice total.

**Independent Test**: Quickstart "After deployment" steps 2 and 3, and the unit tests below.

- [ ] T026 [P] [US4] Write failing tests in `tests/MyGarage.Api.Tests/EmailIntake/ProposalMapperTests.cs`: a negative flat cost is kept; a labor amount with no hours or rate is returned as `LaborCost`; a lone hours or a lone rate is dropped and the labor total kept as `LaborCost` (and when no labor total was given, the line's labor is left empty); hours, rate, and a labor cost together keep hours and rate and drop `LaborCost`; the top-level tax is rounded to cents and kept out of the line items; a flat cost with labor or parts is resolved as today
- [ ] T027 [P] [US4] Write failing tests in `tests/MyGarage.Api.Tests/EmailIntake/ResponsesApiExtractorTests.cs`: the request schema contains `tax` and `lineItems[].laborCost` as required nullable numbers and the instructions contain the discount, tax, and labor rules; a fake model reply with those fields parses into the proposal; a reply that omits them is rejected by the strict schema parse as today
- [ ] T028 [US4] Update `src/MyGarage.Api/EmailIntake/Extraction/ExtractionSchema.cs` (schema and instructions per contracts/pricing-api.md), `IDraftExtractor.cs` (result types gain `Tax` and `LaborCost`), `ResponsesApiExtractor.cs` (map the new fields), and `ProposalMapper.cs` (the guard rules from T026). Run T026 and T027
- [ ] T029 [P] [US4] Write failing tests in `tests/MyGarage.Api.Tests/Drafts/CompatibilityCheckerTests.cs`: a draft whose line items plus tax equal the document total has no mismatch warning; one that differs warns with "The line items and tax add up to X, but the document total is Y." when a tax is proposed and with today's text when none is; a negative flat cost, a labor-cost-only line, and a tax are not reported invalid; `serviceRecord.tax` is listed as ok when proposed; and `DraftServiceTests.cs` (accepting a draft with a discount, a labor total, and a tax creates a record whose total equals the document total)
- [ ] T030 [US4] Update `src/MyGarage.Api/Drafts/DraftProposalMapper.cs` (`ToRequest`, the values passed to acceptance, `LineItemsTotal` plus tax) and `CompatibilityChecker.cs` (warning text and `KnownPaths` for `tax` and `laborCost`). Run T029
- [ ] T031 [P] [US4] Write failing Web tests: `tests/MyGarage.Web.Tests/Drafts/DraftReviewFormModelTests.cs` (the form model round-trips `Tax` and a line's `LaborCost`; the displayed record total includes the tax and negative lines), `ServiceRecordProposalEditorTests.cs` (a Tax input; a "Total labor" input after the rate, disabled and calculated when both are present; a negative flat cost renders as a credit), `DraftReviewPageTests.cs` and `CompatibilityPanelTests.cs` (no mismatch shown for a consistent draft)
- [ ] T032 [US4] Update `src/MyGarage.Web/Drafts/DraftReviewFormModel.cs` and `ServiceRecordProposalEditor.razor` (and `DraftFormat.cs` if it formats amounts) so the editor mirrors the manual forms from US1-US3, including field error paths `serviceRecord.tax` and `serviceRecord.lineItems[i].laborCost`. Run T031

**Checkpoint**: A draft shows and accepts a discount, a labor total, and a tax without edits.

---

## Phase 7: Polish and Cross-Cutting

- [ ] T033 [P] Update `docs/ui-standard.md`: credits (a minus sign and a visually hidden label, never color alone), and calculated versus entered fields (a disabled input that shows the calculated value), with the Tax and Total labor fields as the examples; extend `UiStandardConformanceTests` if it checks forms for the new inputs
- [ ] T034 Run the whole solution: `dotnet build MyGarage.slnx` (zero warnings) and `dotnet test MyGarage.slnx`, confirm migrations apply to an empty database, and fix what fails
- [ ] T035 Run the quickstart local steps with `aspire run` in the integrated browser (ask the owner to sign in first) and record the result
- [ ] T036 Open a pull request for `009-invoice-pricing-rules`; when the owner merges it and approves the `production` gate, open the existing draft from the invoice that raised these issues (or resend it), confirm it shows the discount, the labor total, and the tax with no mismatch warning, accept it, and confirm the record total equals the invoice total (SC-002)

---

## Dependencies and Execution Order

- Phase 1 then Phase 2 (T002-T010). Within Phase 2: T002 before T003; T004 before T005; T006 before T007; T008 before T009; T010 is independent of the database tasks and can run alongside them.
- US1 (T011-T015), US2 (T016-T020), and US3 (T021-T025) each depend only on Phase 2. US1 and US2 both edit `ServiceLineItemRequestValidator.cs` and the line item form, so do them one after the other (US1 first) to avoid conflicts; US3 touches record files only and can run in parallel with either.
- US4 (T026-T032) needs US1-US3 because the draft editor mirrors their fields and its check uses their rules.
- Phase 7 last; T036 happens after merge.
- Within a story, tests first and seen failing, then the implementation task that makes them pass.

## Implementation Strategy

MVP is Phase 2 plus US1: a discount works end to end. Then US2, US3, and US4. Stop and run the full suites after each story. Commit after each task or logical group with Conventional Commit subjects.
