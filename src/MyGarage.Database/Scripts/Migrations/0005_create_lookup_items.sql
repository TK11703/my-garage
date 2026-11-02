CREATE TABLE lookup_items (
    id         uuid        PRIMARY KEY,
    kind       text        NOT NULL CHECK (kind IN ('vehicle_type', 'fuel_type', 'transmission', 'drivetrain')),
    owner_id   text        NULL,
    name       varchar(30) NOT NULL,
    name_key   text        NOT NULL GENERATED ALWAYS AS (lower(name)) STORED,
    sort_order integer     NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL,
    created_by text        NULL,
    CHECK ((owner_id IS NULL) = (created_by IS NULL))
);

CREATE UNIQUE INDEX ux_lookup_items_default ON lookup_items (kind, name_key) WHERE owner_id IS NULL;
CREATE UNIQUE INDEX ux_lookup_items_custom ON lookup_items (owner_id, kind, name_key) WHERE owner_id IS NOT NULL;

CREATE TABLE lookup_item_hidden (
    owner_id  text        NOT NULL,
    item_id   uuid        NOT NULL REFERENCES lookup_items (id) ON DELETE CASCADE,
    hidden_at timestamptz NOT NULL,
    PRIMARY KEY (owner_id, item_id)
);

INSERT INTO lookup_items (id, kind, owner_id, name, sort_order, created_at, created_by) VALUES
    ('01930000-0000-7000-8000-000000000001', 'vehicle_type', NULL, 'Sedan', 1, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000002', 'vehicle_type', NULL, 'SUV', 2, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000003', 'vehicle_type', NULL, 'Coupe', 3, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000004', 'vehicle_type', NULL, 'Hatchback', 4, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000005', 'vehicle_type', NULL, 'Convertible', 5, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000006', 'vehicle_type', NULL, 'Truck', 6, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000007', 'vehicle_type', NULL, 'Van', 7, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000008', 'vehicle_type', NULL, 'Wagon', 8, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000009', 'vehicle_type', NULL, 'Minivan', 9, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000010', 'vehicle_type', NULL, 'Crossover', 10, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000011', 'vehicle_type', NULL, 'Motorcycle', 11, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000021', 'fuel_type', NULL, 'Gasoline', 1, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000022', 'fuel_type', NULL, 'Diesel', 2, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000023', 'fuel_type', NULL, 'Electric', 3, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000024', 'fuel_type', NULL, 'Hybrid', 4, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000031', 'transmission', NULL, 'Automatic', 1, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000032', 'transmission', NULL, 'Manual', 2, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000041', 'drivetrain', NULL, 'FWD', 1, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000042', 'drivetrain', NULL, 'RWD', 2, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000043', 'drivetrain', NULL, 'AWD', 3, '2026-11-01T00:00:00Z', NULL),
    ('01930000-0000-7000-8000-000000000044', 'drivetrain', NULL, '4WD', 4, '2026-11-01T00:00:00Z', NULL);
