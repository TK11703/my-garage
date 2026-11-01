SELECT n.id, n.category, n.body, n.created_at, n.updated_at, n.created_by, n.updated_by
FROM journal_notes n
JOIN vehicles v ON v.id = n.vehicle_id
WHERE n.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId
ORDER BY n.created_at DESC, n.id DESC
LIMIT @PageSize OFFSET @Offset;
