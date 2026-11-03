SELECT count(*)
FROM fuel_energy_entries fe
JOIN vehicles v ON v.id = fe.vehicle_id
WHERE fe.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
