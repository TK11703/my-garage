-- A vehicle is now named from its year, make, and model, so the free-text nickname is no longer stored.
DROP INDEX ix_vehicles_owner_nickname;

ALTER TABLE vehicles DROP COLUMN nickname;

CREATE INDEX ix_vehicles_owner_name ON vehicles (owner_id, lower(make), lower(model), year, id);
