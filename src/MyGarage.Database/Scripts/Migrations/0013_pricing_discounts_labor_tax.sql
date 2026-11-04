-- Discounts, labor totals, and tax on service records (spec 009).
-- Flat cost and line totals may be negative (a discount). A line item can carry an entered labor cost when it has no hours and rate.
-- A service record can carry a tax amount. Both new columns are nullable, and every existing row satisfies the new checks, so no data is rewritten.

ALTER TABLE service_line_items DROP CONSTRAINT service_line_items_flat_cost_check;
ALTER TABLE service_line_items DROP CONSTRAINT service_line_items_total_cost_check;
ALTER TABLE service_line_items ADD CONSTRAINT ck_service_line_items_flat_cost CHECK (flat_cost BETWEEN -9999999.99 AND 9999999.99);
ALTER TABLE service_line_items ADD CONSTRAINT ck_service_line_items_total_cost CHECK (total_cost BETWEEN -9999999.99 AND 9999999.99);

-- labor_cost is only the entered amount; a calculated labor total is derived from labor_hours and labor_rate and is never stored here.
ALTER TABLE service_line_items ADD COLUMN labor_cost numeric(12,2) NULL CHECK (labor_cost BETWEEN 0 AND 9999999.99);
ALTER TABLE service_line_items DROP CONSTRAINT ck_service_line_items_flat_or_breakdown;
ALTER TABLE service_line_items ADD CONSTRAINT ck_service_line_items_flat_or_breakdown
    CHECK (flat_cost IS NULL OR (labor_hours IS NULL AND labor_cost IS NULL));
ALTER TABLE service_line_items ADD CONSTRAINT ck_service_line_items_labor_cost_without_hours
    CHECK (labor_cost IS NULL OR labor_hours IS NULL);

-- The record total is computed (line totals plus this tax), not stored.
ALTER TABLE service_records ADD COLUMN tax_amount numeric(12,2) NULL CHECK (tax_amount BETWEEN 0 AND 9999999.99);
