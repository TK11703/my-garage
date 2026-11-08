UPDATE vehicles
SET archived_at = NULL,
    updated_at = @Now,
    updated_by = @OwnerId
WHERE id = @VehicleId
  AND owner_id = @OwnerId
  AND archived_at IS NOT NULL
RETURNING id;
