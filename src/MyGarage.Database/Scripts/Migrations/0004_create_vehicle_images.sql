CREATE TABLE vehicle_images (
    id           uuid         PRIMARY KEY,
    vehicle_id   uuid         NOT NULL REFERENCES vehicles (id) ON DELETE CASCADE,
    content_type text         NOT NULL,
    size_bytes   integer      NOT NULL CHECK (size_bytes BETWEEN 1 AND 5242880),
    caption      varchar(200) NULL,
    is_main      boolean      NOT NULL DEFAULT false,
    created_at   timestamptz  NOT NULL,
    created_by   text         NOT NULL
);

CREATE INDEX ix_vehicle_images_vehicle ON vehicle_images (vehicle_id, created_at, id);

CREATE UNIQUE INDEX ux_vehicle_images_main ON vehicle_images (vehicle_id) WHERE is_main;
