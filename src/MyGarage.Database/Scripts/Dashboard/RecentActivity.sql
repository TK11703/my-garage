-- The owner's most recent journal entries across all their vehicles, newest first, with the vehicle's main picture for the avatar.
SELECT
    n.id AS note_id,
    n.vehicle_id,
    v.year AS vehicle_year,
    v.make AS vehicle_make,
    v.model AS vehicle_model,
    mi.id AS vehicle_main_image_id,
    n.category,
    n.body,
    n.created_at
FROM journal_notes n
JOIN vehicles v ON v.id = n.vehicle_id
LEFT JOIN vehicle_images mi ON mi.vehicle_id = v.id AND mi.is_main
WHERE v.owner_id = @OwnerId
  AND v.archived_at IS NULL
ORDER BY n.created_at DESC, n.id DESC
LIMIT @Limit;
