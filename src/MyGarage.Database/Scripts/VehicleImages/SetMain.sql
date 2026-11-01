UPDATE vehicle_images
SET is_main = true
WHERE id = @ImageId
  AND vehicle_id = @VehicleId;
