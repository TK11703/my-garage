-- NULL means active; a value is the instant the owner archived the vehicle. Nothing else changes: an archived vehicle keeps every child row.
ALTER TABLE vehicles ADD COLUMN archived_at timestamptz NULL;

CREATE INDEX ix_vehicles_owner_active ON vehicles (owner_id) WHERE archived_at IS NULL;
