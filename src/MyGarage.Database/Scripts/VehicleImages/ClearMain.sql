UPDATE vehicle_images
SET is_main = false
WHERE vehicle_id = @VehicleId
  AND is_main;
