-- An upload is queued as a submission like an email, but its origin is 'upload' and it has no sender, subject, or text.
ALTER TABLE email_submissions ADD COLUMN origin varchar(6) NOT NULL DEFAULT 'email' CHECK (origin IN ('email', 'upload'));

-- The vehicle the owner chose when importing from a vehicle's maintenance tab; kept only for that owner's own vehicle.
ALTER TABLE email_submissions ADD COLUMN preselected_vehicle_id uuid NULL REFERENCES vehicles (id) ON DELETE SET NULL;

-- 'selected' means the owner chose the vehicle when uploading.
ALTER TABLE maintenance_drafts DROP CONSTRAINT maintenance_drafts_vehicle_match_check;

ALTER TABLE maintenance_drafts ADD CONSTRAINT maintenance_drafts_vehicle_match_check
    CHECK (vehicle_match IN ('vin', 'description', 'only_vehicle', 'selected', 'none'));
