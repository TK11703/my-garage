SELECT id
FROM vehicles
WHERE id = @VehicleId
  AND owner_id = @OwnerId
FOR UPDATE;
