-- Known users, so a sender address can be matched; written only from validated claims.
CREATE TABLE user_profiles (
    owner_id      text         PRIMARY KEY,
    email         varchar(320) NULL,
    display_name  varchar(200) NOT NULL,
    first_seen_at timestamptz  NOT NULL,
    last_seen_at  timestamptz  NOT NULL
);

-- Not unique, so an address shared by several profiles is detectable and refused.
CREATE INDEX ix_user_profiles_email ON user_profiles (lower(email));

-- One row per message id: the work queue and the duplicate guard.
CREATE TABLE email_submissions (
    id                  uuid         PRIMARY KEY,
    message_id          varchar(512) NOT NULL UNIQUE,
    status              varchar(14)  NOT NULL CHECK (status IN ('queued', 'processing', 'ready', 'not_applicable', 'not_accepted', 'failed')),
    arrived_at          timestamptz  NOT NULL,
    received_at         timestamptz  NULL,
    owner_id            text         NULL,
    sender              varchar(320) NULL,
    subject             varchar(500) NULL,
    body_text           text         NULL CHECK (char_length(body_text) <= 100000),
    attempt_count       smallint     NOT NULL DEFAULT 0,
    next_attempt_at     timestamptz  NOT NULL,
    locked_until        timestamptz  NULL,
    failure_reason      varchar(200) NULL,
    skipped_attachments jsonb        NOT NULL DEFAULT '[]',
    content_purged_at   timestamptz  NULL
);

CREATE INDEX ix_email_submissions_queue ON email_submissions (next_attempt_at) WHERE status IN ('queued', 'processing');

CREATE INDEX ix_email_submissions_owner ON email_submissions (owner_id, arrived_at DESC, id DESC) WHERE owner_id IS NOT NULL;

CREATE TABLE email_attachments (
    id            uuid         PRIMARY KEY,
    submission_id uuid         NOT NULL REFERENCES email_submissions (id) ON DELETE CASCADE,
    owner_id      text         NOT NULL,
    file_name     varchar(255) NOT NULL,
    content_type  varchar(20)  NOT NULL CHECK (content_type IN ('application/pdf', 'image/jpeg', 'image/png', 'image/webp')),
    size_bytes    integer      NOT NULL CHECK (size_bytes BETWEEN 1 AND 10485760),
    sha256        char(64)     NOT NULL,
    created_at    timestamptz  NOT NULL
);

CREATE INDEX ix_email_attachments_owner_hash ON email_attachments (owner_id, sha256);

-- A proposed record waiting for the owner; nothing becomes a vehicle record until the owner accepts it.
CREATE TABLE maintenance_drafts (
    id                  uuid          PRIMARY KEY,
    owner_id            text          NOT NULL,
    submission_id       uuid          NOT NULL REFERENCES email_submissions (id) ON DELETE CASCADE,
    attachment_id       uuid          NULL REFERENCES email_attachments (id) ON DELETE SET NULL,
    state               varchar(12)   NOT NULL CHECK (state IN ('needs_review', 'manual_entry', 'included', 'discarded', 'expired')),
    kind                varchar(14)   NULL CHECK (kind IN ('service_record', 'fuel', 'energy', 'inspection', 'registration', 'insurance', 'tax')),
    vehicle_id          uuid          NULL REFERENCES vehicles (id) ON DELETE SET NULL,
    vehicle_match       varchar(12)   NOT NULL CHECK (vehicle_match IN ('vin', 'description', 'only_vehicle', 'none')),
    confidence          numeric(3,2)  NULL CHECK (confidence BETWEEN 0 AND 1),
    proposal            jsonb         NOT NULL DEFAULT '{}',
    notes               varchar(500)  NULL,
    created_record_kind varchar(14)   NULL,
    created_record_id   uuid          NULL,
    created_at          timestamptz   NOT NULL,
    expires_at          timestamptz   NOT NULL,
    decided_at          timestamptz   NULL
);

CREATE INDEX ix_maintenance_drafts_owner_open ON maintenance_drafts (owner_id, created_at DESC, id DESC) WHERE state IN ('needs_review', 'manual_entry');

CREATE INDEX ix_maintenance_drafts_expiry ON maintenance_drafts (expires_at) WHERE state IN ('needs_review', 'manual_entry');

CREATE INDEX ix_maintenance_drafts_submission ON maintenance_drafts (submission_id);

CREATE TABLE notifications (
    id         uuid         PRIMARY KEY,
    owner_id   text         NOT NULL,
    kind       varchar(20)  NOT NULL CHECK (kind IN ('draft_ready')),
    draft_id   uuid         NULL REFERENCES maintenance_drafts (id) ON DELETE CASCADE,
    title      varchar(120) NOT NULL,
    created_at timestamptz  NOT NULL,
    read_at    timestamptz  NULL
);

CREATE INDEX ix_notifications_owner ON notifications (owner_id, created_at DESC, id DESC);

CREATE INDEX ix_notifications_unread ON notifications (owner_id) WHERE read_at IS NULL;

-- Originals the owner chose to keep; ownership is proved through the vehicle. source_record_id is informational and has no foreign key.
CREATE TABLE vehicle_documents (
    id               uuid         PRIMARY KEY,
    vehicle_id       uuid         NOT NULL REFERENCES vehicles (id) ON DELETE CASCADE,
    file_name        varchar(255) NOT NULL,
    content_type     varchar(20)  NOT NULL CHECK (content_type IN ('application/pdf', 'image/jpeg', 'image/png', 'image/webp')),
    size_bytes       integer      NOT NULL CHECK (size_bytes BETWEEN 1 AND 10485760),
    source_kind      varchar(14)  NULL,
    source_record_id uuid         NULL,
    created_at       timestamptz  NOT NULL,
    created_by       text         NOT NULL
);

CREATE INDEX ix_vehicle_documents_vehicle ON vehicle_documents (vehicle_id, created_at DESC, id DESC);

-- Set only by the accept transaction; existing rows and code paths read false.
ALTER TABLE service_records ADD COLUMN from_email boolean NOT NULL DEFAULT false;

ALTER TABLE fuel_energy_entries ADD COLUMN from_email boolean NOT NULL DEFAULT false;

ALTER TABLE inspections ADD COLUMN from_email boolean NOT NULL DEFAULT false;

ALTER TABLE registrations ADD COLUMN from_email boolean NOT NULL DEFAULT false;

ALTER TABLE insurance_policies ADD COLUMN from_email boolean NOT NULL DEFAULT false;

ALTER TABLE taxes ADD COLUMN from_email boolean NOT NULL DEFAULT false;
