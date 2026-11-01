UPDATE journal_notes n
SET category   = @Category,
    body       = @Body,
    updated_at = @Now,
    updated_by = @OwnerId
FROM vehicles v
WHERE n.id = @NoteId
  AND n.vehicle_id = @VehicleId
  AND v.id = n.vehicle_id
  AND v.owner_id = @OwnerId
RETURNING n.id, n.category, n.body, n.created_at, n.updated_at, n.created_by, n.updated_by;
