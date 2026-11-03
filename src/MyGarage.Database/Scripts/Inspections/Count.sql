SELECT count(*)
FROM inspections i
JOIN vehicles v ON v.id = i.vehicle_id
WHERE i.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
