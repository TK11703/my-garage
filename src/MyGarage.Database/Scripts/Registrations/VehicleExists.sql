SELECT EXISTS (
    SELECT 1
    FROM vehicles
    WHERE id = @VehicleId
      AND owner_id = @OwnerId);
