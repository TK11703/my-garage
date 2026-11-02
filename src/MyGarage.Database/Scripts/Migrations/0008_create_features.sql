-- Features are shared: one catalog entry per name (ignoring case), linked to any number of vehicles by vehicle_tags.
CREATE TABLE features (
    id         uuid        PRIMARY KEY,
    name       varchar(30) NOT NULL,
    name_key   text        NOT NULL GENERATED ALWAYS AS (lower(name)) STORED,
    created_at timestamptz NOT NULL,
    created_by text        NOT NULL
);

CREATE UNIQUE INDEX ux_features_name ON features (name_key);

INSERT INTO features (id, name, created_at, created_by)
SELECT DISTINCT ON (t.name_key) gen_random_uuid(), t.name, t.created_at, v.owner_id
FROM vehicle_tags t
JOIN vehicles v ON v.id = t.vehicle_id
ORDER BY t.name_key, t.created_at;

ALTER TABLE vehicle_tags ADD COLUMN feature_id uuid NULL REFERENCES features (id) ON DELETE RESTRICT;

UPDATE vehicle_tags t
SET feature_id = f.id
FROM features f
WHERE f.name_key = t.name_key;

-- name_key is generated from name, so it has to go first.
ALTER TABLE vehicle_tags DROP COLUMN name_key;

ALTER TABLE vehicle_tags
    ALTER COLUMN feature_id SET NOT NULL,
    DROP COLUMN name,
    ADD CONSTRAINT ux_vehicle_tags_vehicle_feature UNIQUE (vehicle_id, feature_id);

CREATE INDEX ix_vehicle_tags_feature ON vehicle_tags (feature_id);
