# Research: Discounts, Labor Totals, and Tax on Service Records

Decisions that shape the plan. Each lists what was chosen, why, and what was rejected.

## R1. Discount representation

**Decision**: A discount is an ordinary line item with a negative flat cost, within -9,999,999.99 to 9,999,999.99.

**Why**: The owner chose it. It needs no new column, the extractor can return the amount as printed, and the existing "flat cost excludes labor and parts" rule already keeps a discount line simple.

**Rejected**: A line `kind` (charge, discount, tax) with positive amounts. Clearer in the form, but it adds a column, a mapping layer, and an extractor rule for no new capability.

## R2. Labor total

**Decision**: Add `labor_cost` (nullable, 0 to 9,999,999.99) to the line item. It is stored only when the user enters it, which is only allowed when `labor_hours` and `labor_rate` are both absent. When hours and rate are both present the labor cost is calculated (`ServiceCostCalculator.LaborCost`) and nothing is stored in `labor_cost`.

**Why**: The calculated value is derived, so storing it would duplicate it. The DTO exposes one `LaborCost` that is the calculated value or the stored entry, so the UI shows a single "Total labor" in both cases.

**Consequences**: The line total becomes flat cost, or effective labor cost plus parts. The existing CHECK that a line cannot hold both a flat cost and labor hours is extended to cover `labor_cost`. A request with hours, rate, and a labor cost is accepted and the typed labor cost is ignored (the calculation wins, per the spec); a request with only one of hours or rate is rejected, as today.

**Rejected**: A single stored `labor_total` always written (more code to keep equal to hours x rate); deriving the rate from a total and hours (invents data the invoice did not give).

## R3. Tax placement

**Decision**: `service_records.tax_amount` (nullable, 0 to 9,999,999.99), exposed as `Tax` on the request and the record DTO. The record total is `sum(line totals) + coalesce(tax, 0)`, computed by the same two SQL aggregates that already compute it (`GetById.sql`, `ListPage.sql`) and by `FindDuplicateRecord.sql`.

**Why**: The owner chose a record-level field. It keeps tax out of the line item rules and the 25-line limit, and one field is enough because a record has one tax total.

**Rejected**: A tax line item (loses the "separate from the items" display and mixes with discounts); a separate tax table (nothing to relate).

## R4. Negative totals

**Decision**: Do not enforce a non-negative record total. `service_line_items.total_cost` is relaxed to the same symmetric range as flat cost; a record total is not constrained.

**Why**: Lines and the tax are written by separate requests, so any record-level rule would depend on the order the user edits. A refund-only record is also real. Totals display as credits when below zero.

**Rejected**: Rejecting a write that would leave the total below zero (traps valid edit orders); a database trigger (hidden behavior, harder to test).

## R5. Migration

**Decision**: `0013_pricing_discounts_labor_tax.sql`, forward-only. It drops and recreates the two named CHECKs with the wider range (naming them explicitly), adds the two columns with their CHECKs, and adds `ck_service_line_items_flat_or_breakdown` replacement that also excludes `labor_cost` with `flat_cost`. Existing rows satisfy the new constraints, so nothing is rewritten.

**Why**: Constitution VII forbids editing `0009`. The CI migrations job already fails a change to an applied script.

**Check needed**: the generated names of the unnamed CHECKs on `flat_cost` and `total_cost` in `0009`; the migration test must read them from `pg_constraint` rather than assume.

## R6. Extraction

**Decision**: The structured-output schema gains a top-level `tax` (number or null) and a per-line `laborCost` (number or null). The instructions say: a discount is a negative flat cost; tax goes in `tax` and is never a line item; give labor hours and rate only when both are printed, otherwise give the labor total in `laborCost`. The mapper enforces the same rules afterwards, so a model that returns a lone hours or rate is corrected, not trusted.

**Why**: Everything the model returns is untrusted input (spec 007 R8); the mapper is the guard, the prompt only reduces how often it has to act.

**Rejected**: Relying on the prompt alone.

## R7. Compatibility check

**Decision**: The document-total warning compares the invoice total with line items plus tax, and `serviceRecord.tax` appears in the known field paths when a tax is proposed. Nothing about a discount, labor total, or tax is an error because the same validators run on the proposal.

## R8. Display of credits

**Decision**: Negative amounts render through the existing money formatter with a leading minus sign, and the description is shown as typed. A visually hidden "credit" label is added for assistive technology.

**Why**: The UI standard forbids meaning carried by color alone; the minus sign and the hidden label carry it.
