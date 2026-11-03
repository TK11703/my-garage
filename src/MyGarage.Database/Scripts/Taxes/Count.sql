SELECT count(*)
FROM taxes t
JOIN vehicles v ON v.id = t.vehicle_id
WHERE t.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
