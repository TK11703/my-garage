CREATE TABLE IF NOT EXISTS schema_migrations (
    script_id  text        PRIMARY KEY,
    checksum   text        NOT NULL,
    applied_at timestamptz NOT NULL
);
