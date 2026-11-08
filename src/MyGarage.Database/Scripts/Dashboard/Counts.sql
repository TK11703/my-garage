-- One row of owner-scoped counts. Drafts awaiting review follow Notifications/CountDraftsAwaitingReview.sql.
SELECT
    (SELECT count(*)::int FROM vehicles v WHERE v.owner_id = @OwnerId) AS vehicles,
    (SELECT count(*)::int FROM service_records sr JOIN vehicles v ON v.id = sr.vehicle_id WHERE v.owner_id = @OwnerId) AS service_records,
    (SELECT count(*)::int FROM fuel_energy_entries fe JOIN vehicles v ON v.id = fe.vehicle_id WHERE v.owner_id = @OwnerId) AS fuel_energy_entries,
    (SELECT count(*)::int FROM maintenance_drafts d WHERE d.owner_id = @OwnerId AND d.state IN ('needs_review', 'manual_entry')) AS drafts_awaiting_review;
