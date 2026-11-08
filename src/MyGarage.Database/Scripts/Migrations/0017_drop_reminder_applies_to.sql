-- A reminder no longer says what it applies to: the name says it. An equipment or location name moves into the reminder's name.
UPDATE reminders
SET name = left(name || ' (' || subject || ')', 100)
WHERE subject IS NOT NULL;

ALTER TABLE reminders DROP CONSTRAINT reminders_subject_check;
ALTER TABLE reminders DROP COLUMN applies_to, DROP COLUMN subject;
