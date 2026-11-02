CREATE TABLE service_records (
    id               uuid         PRIMARY KEY,
    vehicle_id       uuid         NOT NULL REFERENCES vehicles (id) ON DELETE CASCADE,
    date_serviced    date         NOT NULL CHECK (date_serviced >= DATE '1900-01-01'),
    odometer_reading integer      NOT NULL CHECK (odometer_reading BETWEEN 0 AND 9999999),
    shop_or_person   varchar(100) NULL,
    created_at       timestamptz  NOT NULL,
    updated_at       timestamptz  NOT NULL,
    created_by       text         NOT NULL,
    updated_by       text         NOT NULL
);

CREATE INDEX ix_service_records_vehicle_serviced ON service_records (vehicle_id, date_serviced DESC, created_at DESC, id DESC);

-- total_cost is computed by the repository on every write (flat cost, or labor plus parts).
CREATE TABLE service_line_items (
    id                uuid          PRIMARY KEY,
    service_record_id uuid          NOT NULL REFERENCES service_records (id) ON DELETE CASCADE,
    description       varchar(200)  NOT NULL,
    flat_cost         numeric(12,2) NULL CHECK (flat_cost BETWEEN 0 AND 9999999.99),
    labor_hours       numeric(5,2)  NULL CHECK (labor_hours BETWEEN 0.01 AND 999.99),
    labor_rate        numeric(7,2)  NULL CHECK (labor_rate BETWEEN 0 AND 99999.99),
    total_cost        numeric(12,2) NOT NULL CHECK (total_cost BETWEEN 0 AND 9999999.99),
    created_at        timestamptz   NOT NULL,
    updated_at        timestamptz   NOT NULL,
    created_by        text          NOT NULL,
    updated_by        text          NOT NULL,
    CONSTRAINT ck_service_line_items_labor_pair CHECK ((labor_hours IS NULL) = (labor_rate IS NULL)),
    CONSTRAINT ck_service_line_items_flat_or_breakdown CHECK (flat_cost IS NULL OR labor_hours IS NULL)
);

CREATE INDEX ix_service_line_items_record ON service_line_items (service_record_id, created_at, id);

CREATE TABLE service_line_item_parts (
    id           uuid          PRIMARY KEY,
    line_item_id uuid          NOT NULL REFERENCES service_line_items (id) ON DELETE CASCADE,
    position     smallint      NOT NULL,
    name         varchar(100)  NOT NULL,
    quantity     numeric(6,2)  NOT NULL CHECK (quantity BETWEEN 0.01 AND 9999.99),
    unit_price   numeric(12,2) NOT NULL CHECK (unit_price BETWEEN 0 AND 9999999.99),
    CONSTRAINT ux_service_line_item_parts_position UNIQUE (line_item_id, position)
);
