CREATE TABLE journal_notes (
    id         uuid          PRIMARY KEY,
    vehicle_id uuid          NOT NULL REFERENCES vehicles (id) ON DELETE CASCADE,
    category   varchar(40)   NULL,
    body       varchar(4000) NOT NULL,
    created_at timestamptz   NOT NULL,
    updated_at timestamptz   NOT NULL,
    created_by text          NOT NULL,
    updated_by text          NOT NULL
);

CREATE INDEX ix_journal_notes_vehicle_created ON journal_notes (vehicle_id, created_at DESC, id DESC);
