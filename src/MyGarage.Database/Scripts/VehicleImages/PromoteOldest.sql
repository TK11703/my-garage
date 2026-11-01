UPDATE vehicle_images
SET is_main = true
WHERE id = (SELECT id
            FROM vehicle_images
            WHERE vehicle_id = @VehicleId
            ORDER BY created_at, id
            LIMIT 1);
