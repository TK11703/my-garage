SELECT t.id, t.name, t.created_at
FROM vehicle_tags t
JOIN vehicles v ON v.id = t.vehicle_id
WHERE t.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId
ORDER BY t.created_at, t.id;
