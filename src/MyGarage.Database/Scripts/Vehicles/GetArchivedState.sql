-- Null when the vehicle does not exist for that owner; otherwise whether it is archived.
SELECT archived_at IS NOT NULL
FROM vehicles
WHERE id = @VehicleId
  AND owner_id = @OwnerId;
