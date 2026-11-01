CREATE TABLE vehicle_tags (
    id         uuid        PRIMARY KEY,
    vehicle_id uuid        NOT NULL REFERENCES vehicles (id) ON DELETE CASCADE,
    name       varchar(30) NOT NULL,
    name_key   text        NOT NULL GENERATED ALWAYS AS (lower(name)) STORED,
    created_at timestamptz NOT NULL,
    UNIQUE (vehicle_id, name_key)
);
