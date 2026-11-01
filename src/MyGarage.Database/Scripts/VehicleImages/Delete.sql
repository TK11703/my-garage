DELETE FROM vehicle_images i
USING vehicles v
WHERE i.id = @ImageId
  AND i.vehicle_id = @VehicleId
  AND v.id = i.vehicle_id
  AND v.owner_id = @OwnerId
RETURNING i.is_main;
