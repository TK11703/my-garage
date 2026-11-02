SELECT count(*)
FROM service_records sr
JOIN vehicles v ON v.id = sr.vehicle_id
WHERE sr.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
