# Implementation Plan: Discounts, Labor Totals, and Tax on Service Records

**Branch**: `009-invoice-pricing-rules` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/009-invoice-pricing-rules/spec.md`

## Summary

Real invoices broke three assumptions of the service record pricing model: a flat cost is never negative, labor always has hours and a rate, and there is no place for tax. This feature relaxes and extends the model in place:

- A line item's flat cost may be negative (a discount), within +/-9,999,999.99. Line totals may therefore be negative and a record's total may be negative.
- A line item gains a **labor cost** that is calculated from hours and rate when both are present, and entered directly when neither is. Hours without a rate (or the reverse) stays an error in the form and API.
- A service record gains an optional **tax** amount. The record total is the line totals plus the tax; tax is not a line item.
- The email draft path carries the same three things: the extractor returns tax in its own field and a labor amount without hours or rate, the mapper drops a lone hours or rate, and the document-total check compares against line items plus tax.

Technical approach: one forward-only migration (`0013`) that relaxes two CHECK constraints and adds two nullable columns (`service_records.tax_amount`, `service_line_items.labor_cost`); existing rows are not touched. The calculator and validators change; two SQL aggregates add the tax; DTOs, forms, rows, and the draft editor gain the new fields. No new package, project, endpoint, or table.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (pinned by `global.json`)

**Primary Dependencies**: Existing only (FluentValidation, Npgsql, Dapper, Blazor, Bootstrap 5.3.x). **New**: none.

**Storage**: PostgreSQL, migration `0013_pricing_discounts_labor_tax.sql`: relax `service_line_items` CHECKs on `flat_cost` and `total_cost`; add `service_records.tax_amount numeric(12,2) NULL CHECK (tax_amount BETWEEN 0 AND 9999999.99)` and `service_line_items.labor_cost numeric(12,2) NULL CHECK (labor_cost BETWEEN 0 AND 9999999.99)`; add a CHECK that `labor_cost` is only set when `labor_hours` is null.

**Testing**: xUnit; Testcontainers PostgreSQL for `MyGarage.Database.Tests` and `MyGarage.Api.Tests` (no mocked database); bUnit and `WebApplicationFactory` for `MyGarage.Web.Tests`; fake extractor and fake `HttpMessageHandler` for the AI boundary. Docker required.

**Target Platform**: Local development with Aspire; the deployed Azure Container Apps environment (spec 008) for the post-merge check.

**Project Type**: Web application (Blazor Web App + minimal API + PostgreSQL), Aspire-orchestrated.

**Performance Goals**: No change; the added column and one addition in two aggregates are negligible.

**Constraints**: Constitution rules unchanged: DTOs at the boundary, FluentValidation on every request, parameterized SQL in `.sql` files, forward-only migrations (never edit `0001`-`0012`), async with `CancellationToken`, owner scoping in SQL. The relaxed constraints must keep every existing row valid, so no data rewrite.

**Scale/Scope**: About 35 touched files across Database, Shared, Api, and Web, plus the matching tests; one migration; no new endpoint, entity, or table.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. .NET 10 / C# only; nullable on; warnings as errors; async + `CancellationToken` | PASS | Changes are inside existing async methods; nullable amounts use `decimal?`. |
| II. Feature-folder organization | PASS | Every change lands in the existing `ServiceRecords/`, `Drafts/`, and `EmailIntake/Extraction/` folders of each project and their test mirrors. |
| III. Solution structure & dependency direction | PASS | No new project or reference. Database still does not reference Shared; draft proposals stay JSON text serialized by the Api. |
| IV. DTO boundary | PASS | New fields are added to the Shared request and response DTOs and mapped explicitly; database records stay internal. |
| V. Validated minimal APIs | PASS | The existing validators gain rules and tests; no endpoint is added, so the group, authorization, `ProblemDetails`, and OpenAPI metadata are unchanged except the new fields appear in the schemas. |
| VI. Dapper, PostgreSQL, SQL scripts | PASS | Only `.sql` files change (parameters added, aggregates add the tax). No string SQL in C#. Owner scoping clauses are untouched. |
| VII. Versioned migrations | PASS | One new forward-only migration `0013`; applied migrations are not edited. The CI migrations job guards this. |
| VIII. Test-first, real dependencies | PASS | Failing tests first for the migration, calculator, repository, validators, endpoints, extraction mapper, compatibility check, and Web forms. Real PostgreSQL; only the AI boundary is faked. |
| Auth and data protection | PASS | Ownership comes from claims as before; the new columns sit on rows already scoped by owner; no endpoint gains or loses authorization. |
| Dependencies justified | PASS | No new package. |

**Post-design re-check (after Phase 1)**: unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/009-invoice-pricing-rules/
├── plan.md              # This file
├── research.md          # Phase 0 decisions
├── data-model.md        # Phase 1: columns, constraints, field rules
├── quickstart.md        # Phase 1: how to verify locally and after deployment
├── contracts/
│   └── pricing-api.md   # Request and response changes, extraction schema change
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── MyGarage.Database/
│   ├── Scripts/Migrations/0013_pricing_discounts_labor_tax.sql          # new
│   ├── Scripts/ServiceRecords/{Insert,Update,GetById,ListPage,InsertLineItem,UpdateLineItem}.sql
│   ├── Scripts/Drafts/FindDuplicateRecord.sql                            # total includes tax
│   ├── ServiceRecords/{ServiceCostCalculator,ServiceLineItemInput,ServiceLineItemRecord,ServiceRecordRecord,ServiceRecordRepository}.cs
│   └── Drafts/{AcceptedRecordValues,DraftAcceptanceRepository}.cs
├── MyGarage.Shared/
│   ├── ServiceRecords/{ServiceRecordRequest,ServiceRecordDto,ServiceRecordSummaryDto,ServiceLineItemRequest,ServiceLineItemDto}.cs
│   └── Drafts/DraftProposal.cs                                           # ServiceRecordProposal gains Tax
├── MyGarage.Api/
│   ├── ServiceRecords/{ServiceRecordRequestValidator,ServiceLineItemRequestValidator,ServiceRecordMapper}.cs
│   ├── Drafts/{DraftProposalMapper,CompatibilityChecker}.cs
│   └── EmailIntake/Extraction/{ExtractionSchema,IDraftExtractor,ResponsesApiExtractor,ProposalMapper}.cs
└── MyGarage.Web/
    ├── ServiceRecords/{ServiceRecordFormModel,ServiceLineItemFormModel,ServiceLineItemForm.razor,ServiceLineItemRow.razor,ServiceRecordFormat,...detail and list components}
    └── Drafts/{DraftReviewFormModel,ServiceRecordProposalEditor.razor}

tests/  (mirrors the feature folders of each project under test)
docs/ui-standard.md               # credits (negative amounts) and calculated vs entered fields
```

**Structure Decision**: no new files outside the migration, its test, and this spec folder; everything else extends existing feature-folder files.

## Complexity Tracking

| Item | Why it is needed | Simpler alternative rejected because |
|---|---|---|
| Total-below-zero is allowed | Lines are added and removed one at a time, so a "total never below zero" rule would reject legitimate edit orders (remove a charge before its discount) | A record-level rule would need transactional multi-line editing, which the model does not have |
| `labor_cost` stores only the entered amount | A calculated labor total is derived from hours and rate, so storing it too would duplicate it and could drift | Storing the effective labor cost in every case needs a trigger or a second code path to keep it equal to hours x rate |
