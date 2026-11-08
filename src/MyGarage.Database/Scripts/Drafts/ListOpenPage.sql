-- The owner's drafts awaiting review, newest first. The vehicle comes only from an active vehicle the owner owns; an archived one reads as no vehicle.
SELECT d.id, d.state, d.kind, v.id AS vehicle_id,
       v.year AS vehicle_year, v.make AS vehicle_make, v.model AS vehicle_model,
       d.proposal::text AS proposal, s.arrived_at, d.expires_at, s.origin
FROM maintenance_drafts d
JOIN email_submissions s ON s.id = d.submission_id
LEFT JOIN vehicles v ON v.id = d.vehicle_id AND v.owner_id = d.owner_id AND v.archived_at IS NULL
WHERE d.owner_id = @OwnerId
  AND d.state IN ('needs_review', 'manual_entry')
ORDER BY d.created_at DESC, d.id DESC
LIMIT @PageSize OFFSET @Offset;
