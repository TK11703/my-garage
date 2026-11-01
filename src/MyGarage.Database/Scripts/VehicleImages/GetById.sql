SELECT i.id, i.content_type, i.size_bytes, i.caption, i.is_main, i.created_at
FROM vehicle_images i
JOIN vehicles v ON v.id = i.vehicle_id
WHERE i.id = @ImageId
  AND i.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
