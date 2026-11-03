CREATE TABLE fuel_energy_entries (
    id          uuid          PRIMARY KEY,
    vehicle_id  uuid          NOT NULL REFERENCES vehicles (id) ON DELETE CASCADE,
    kind        varchar(6)    NOT NULL CHECK (kind IN ('fuel', 'energy')),
    entry_date  date          NOT NULL CHECK (entry_date >= DATE '1900-01-01'),
    unit_price  numeric(8,4)  NOT NULL CHECK (unit_price BETWEEN 0 AND 9999.9999),
    quantity    numeric(8,3)  NOT NULL CHECK (quantity BETWEEN 0.001 AND 99999.999),
    total_cost  numeric(12,2) NOT NULL CHECK (total_cost BETWEEN 0 AND 9999999.99),
    created_at  timestamptz   NOT NULL,
    updated_at  timestamptz   NOT NULL,
    created_by  text          NOT NULL,
    updated_by  text          NOT NULL
);

CREATE INDEX ix_fuel_energy_entries_vehicle_date ON fuel_energy_entries (vehicle_id, entry_date DESC, created_at DESC, id DESC);

CREATE TABLE inspections (
    id              uuid          PRIMARY KEY,
    vehicle_id      uuid          NOT NULL REFERENCES vehicles (id) ON DELETE CASCADE,
    inspection_type varchar(9)    NOT NULL CHECK (inspection_type IN ('safety', 'emissions')),
    inspection_date date          NOT NULL CHECK (inspection_date >= DATE '1900-01-01'),
    cost            numeric(12,2) NOT NULL CHECK (cost BETWEEN 0 AND 9999999.99),
    next_due_date   date          NULL CHECK (next_due_date IS NULL OR (next_due_date >= inspection_date AND next_due_date <= DATE '2100-12-31')),
    created_at      timestamptz   NOT NULL,
    updated_at      timestamptz   NOT NULL,
    created_by      text          NOT NULL,
    updated_by      text          NOT NULL
);

CREATE INDEX ix_inspections_vehicle_date ON inspections (vehicle_id, inspection_date DESC, created_at DESC, id DESC);

CREATE TABLE registrations (
    id              uuid          PRIMARY KEY,
    vehicle_id      uuid          NOT NULL REFERENCES vehicles (id) ON DELETE CASCADE,
    state           char(2)       NOT NULL CHECK (state ~ '^[A-Z]{2}$'),
    cost            numeric(12,2) NOT NULL CHECK (cost BETWEEN 0 AND 9999999.99),
    expiration_date date          NOT NULL CHECK (expiration_date BETWEEN DATE '1900-01-01' AND DATE '2100-12-31'),
    created_at      timestamptz   NOT NULL,
    updated_at      timestamptz   NOT NULL,
    created_by      text          NOT NULL,
    updated_by      text          NOT NULL
);

CREATE INDEX ix_registrations_vehicle_expiration ON registrations (vehicle_id, expiration_date DESC, created_at DESC, id DESC);

-- duration is how often the premium is paid; cost is the premium paid each time.
CREATE TABLE insurance_policies (
    id          uuid          PRIMARY KEY,
    vehicle_id  uuid          NOT NULL REFERENCES vehicles (id) ON DELETE CASCADE,
    start_date  date          NOT NULL CHECK (start_date BETWEEN DATE '1900-01-01' AND DATE '2100-12-31'),
    end_date    date          NOT NULL CHECK (end_date BETWEEN DATE '1900-01-01' AND DATE '2100-12-31' AND end_date > start_date),
    duration    varchar(9)    NOT NULL CHECK (duration IN ('monthly', 'bi_annual', 'annual')),
    cost        numeric(12,2) NOT NULL CHECK (cost BETWEEN 0 AND 9999999.99),
    created_at  timestamptz   NOT NULL,
    updated_at  timestamptz   NOT NULL,
    created_by  text          NOT NULL,
    updated_by  text          NOT NULL
);

CREATE INDEX ix_insurance_policies_vehicle_end ON insurance_policies (vehicle_id, end_date DESC, created_at DESC, id DESC);

CREATE TABLE taxes (
    id          uuid          PRIMARY KEY,
    vehicle_id  uuid          NOT NULL REFERENCES vehicles (id) ON DELETE CASCADE,
    tax_type    varchar(17)   NOT NULL CHECK (tax_type IN ('personal_property', 'lease', 'purchase')),
    amount      numeric(12,2) NOT NULL CHECK (amount BETWEEN 0.01 AND 9999999.99),
    recurrence  varchar(9)    NOT NULL CHECK (recurrence IN ('once', 'monthly', 'bi_annual', 'annual')),
    created_at  timestamptz   NOT NULL,
    updated_at  timestamptz   NOT NULL,
    created_by  text          NOT NULL,
    updated_by  text          NOT NULL
);

CREATE INDEX ix_taxes_vehicle_created ON taxes (vehicle_id, created_at DESC, id DESC);
