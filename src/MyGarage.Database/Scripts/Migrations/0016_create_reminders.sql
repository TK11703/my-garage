-- A recurring item on one vehicle. Status, next due mileage, and next due date are derived on read and never stored.
CREATE TABLE reminders (
    id                uuid         PRIMARY KEY,
    vehicle_id        uuid         NOT NULL REFERENCES vehicles (id) ON DELETE CASCADE,
    name              varchar(100) NOT NULL CHECK (btrim(name) <> ''),
    applies_to        varchar(9)   NOT NULL CHECK (applies_to IN ('vehicle', 'equipment', 'location')),
    subject           varchar(100) NULL,
    interval_miles    integer      NULL CHECK (interval_miles BETWEEN 1 AND 999999),
    interval_months   integer      NULL CHECK (interval_months BETWEEN 1 AND 600),
    last_done_on      date         NOT NULL CHECK (last_done_on >= DATE '1900-01-01'),
    last_done_mileage integer      NULL CHECK (last_done_mileage BETWEEN 0 AND 9999999),
    is_paused         boolean      NOT NULL DEFAULT false,
    -- The last state the owner was told about, so each state change notifies once.
    notified_state    varchar(8)   NULL CHECK (notified_state IN ('due_soon', 'overdue')),
    created_at        timestamptz  NOT NULL,
    updated_at        timestamptz  NOT NULL,
    created_by        text         NOT NULL,
    updated_by        text         NOT NULL,
    CONSTRAINT reminders_subject_check CHECK (
        (applies_to = 'vehicle' AND subject IS NULL)
        OR (applies_to <> 'vehicle' AND subject IS NOT NULL AND btrim(subject) <> '')),
    CONSTRAINT reminders_interval_check CHECK (interval_miles IS NOT NULL OR interval_months IS NOT NULL),
    CONSTRAINT reminders_mileage_required_check CHECK (interval_miles IS NULL OR last_done_mileage IS NOT NULL)
);

CREATE INDEX ix_reminders_vehicle ON reminders (vehicle_id, created_at, id);

CREATE TABLE reminder_completions (
    id          uuid        PRIMARY KEY,
    reminder_id uuid        NOT NULL REFERENCES reminders (id) ON DELETE CASCADE,
    done_on     date        NOT NULL CHECK (done_on >= DATE '1900-01-01'),
    mileage     integer     NULL CHECK (mileage BETWEEN 0 AND 9999999),
    created_at  timestamptz NOT NULL,
    created_by  text        NOT NULL
);

CREATE INDEX ix_reminder_completions_reminder ON reminder_completions (reminder_id, done_on DESC, created_at DESC, id DESC);

-- Reminder alerts share the notifications table, bell, and dismiss flow.
ALTER TABLE notifications DROP CONSTRAINT notifications_kind_check;

ALTER TABLE notifications ADD CONSTRAINT notifications_kind_check
    CHECK (kind IN ('draft_ready', 'reminder_due_soon', 'reminder_overdue'));

ALTER TABLE notifications ADD COLUMN reminder_id uuid NULL REFERENCES reminders (id) ON DELETE CASCADE;

ALTER TABLE notifications ADD CONSTRAINT notifications_shape_check CHECK (
    (kind = 'draft_ready' AND reminder_id IS NULL)
    OR (kind <> 'draft_ready' AND reminder_id IS NOT NULL AND draft_id IS NULL));

CREATE INDEX ix_notifications_reminder ON notifications (reminder_id) WHERE reminder_id IS NOT NULL;