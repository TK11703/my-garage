SELECT count(*)
FROM registrations r
JOIN vehicles v ON v.id = r.vehicle_id
WHERE r.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
