-- One row of owner-scoped counts. Drafts awaiting review follow Notifications/CountDraftsAwaitingReview.sql.
-- Archived vehicles leave the vehicle and service record counts; fuel and charging entries keep them (they were real spend), and a draft belongs to the owner, not a vehicle.
SELECT
    (SELECT count(*)::int FROM vehicles v WHERE v.owner_id = @OwnerId AND v.archived_at IS NULL) AS vehicles,
    (SELECT count(*)::int FROM service_records sr JOIN vehicles v ON v.id = sr.vehicle_id WHERE v.owner_id = @OwnerId AND v.archived_at IS NULL) AS service_records,
    (SELECT count(*)::int FROM fuel_energy_entries fe JOIN vehicles v ON v.id = fe.vehicle_id WHERE v.owner_id = @OwnerId) AS fuel_energy_entries,
    (SELECT count(*)::int FROM maintenance_drafts d WHERE d.owner_id = @OwnerId AND d.state IN ('needs_review', 'manual_entry')) AS drafts_awaiting_review;
