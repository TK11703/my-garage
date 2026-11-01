CREATE TABLE vehicles (
    id         uuid         PRIMARY KEY,
    owner_id   text         NOT NULL,
    nickname   varchar(100) NOT NULL,
    make       varchar(50)  NOT NULL,
    model      varchar(50)  NOT NULL,
    trim       varchar(50)  NULL,
    color      varchar(30)  NULL,
    mileage    integer      NULL CHECK (mileage BETWEEN 0 AND 9999999),
    doors      smallint     NULL CHECK (doors BETWEEN 1 AND 6),
    created_at timestamptz  NOT NULL,
    updated_at timestamptz  NOT NULL,
    created_by text         NOT NULL,
    updated_by text         NOT NULL
);

CREATE INDEX ix_vehicles_owner_nickname ON vehicles (owner_id, lower(nickname), id);
