# Data Model: Discounts, Labor Totals, and Tax on Service Records

## Changed tables (migration `0013_pricing_discounts_labor_tax.sql`)

### `service_line_items`

| Column | Before | After |
|---|---|---|
| `flat_cost` | `numeric(12,2) NULL CHECK (BETWEEN 0 AND 9999999.99)` | same type, `CHECK (BETWEEN -9999999.99 AND 9999999.99)` |
| `total_cost` | `numeric(12,2) NOT NULL CHECK (BETWEEN 0 AND 9999999.99)` | same type, `CHECK (BETWEEN -9999999.99 AND 9999999.99)` |
| `labor_cost` | n/a | **new** `numeric(12,2) NULL CHECK (labor_cost BETWEEN 0 AND 9999999.99)`; the entered labor total, stored only when `labor_hours` and `labor_rate` are null |
| `ck_service_line_items_labor_pair` | hours and rate both null or both not null | unchanged |
| `ck_service_line_items_flat_or_breakdown` | `flat_cost IS NULL OR labor_hours IS NULL` | `flat_cost IS NULL OR (labor_hours IS NULL AND labor_cost IS NULL)` |
| new `ck_service_line_items_labor_cost_without_hours` | n/a | `labor_cost IS NULL OR labor_hours IS NULL` |

### `service_records`

| Column | After |
|---|---|
| `tax_amount` | **new** `numeric(12,2) NULL CHECK (tax_amount BETWEEN 0 AND 9999999.99)` |

Existing rows keep `tax_amount` and `labor_cost` null and satisfy every relaxed or new constraint, so nothing is rewritten. There is no stored record total; it is computed.

## Computed values

- **Effective labor cost** of a line: `round(labor_hours * labor_rate, 2)` when both are present, otherwise `labor_cost`, otherwise none.
- **Line total** (`total_cost`, written by the repository on every write): `flat_cost` when present, otherwise effective labor cost plus the sum of `round(quantity * unit_price, 2)` over the parts. Rounding is half away from zero.
- **Record total**: `sum(line total) + coalesce(tax_amount, 0)`, in `GetById.sql`, `ListPage.sql`, and the duplicate lookup `FindDuplicateRecord.sql`.

## Field rules (validators)

| Field | Rule |
|---|---|
| Line `FlatCost` | -9,999,999.99 to 9,999,999.99, at most 2 decimals; excludes labor, labor cost, and parts |
| Line `LaborHours` | 0.01 to 999.99, 2 decimals; requires a rate |
| Line `LaborRate` | 0 to 99,999.99, 2 decimals; requires hours |
| Line `LaborCost` | 0 to 9,999,999.99, 2 decimals; ignored (not stored) when hours and rate are both present; excludes a flat cost |
| Line pricing | exactly one of: flat cost; or at least one of labor (hours and rate, or labor cost) and parts |
| Line total | between -9,999,999.99 and 9,999,999.99 |
| Record `Tax` | 0 to 9,999,999.99, 2 decimals; optional; empty and 0 both mean no tax |

## Draft proposal (stored as JSON, no migration)

`ServiceRecordProposal` gains `Tax` (decimal, optional). Each proposed line item gains `LaborCost` (decimal, optional). Existing stored drafts deserialize with both absent.

## State transitions

None new. A line item's pricing mode is implied by which fields are present; changing it replaces the stored fields in one update, as today.
