SELECT count(*)
FROM insurance_policies p
JOIN vehicles v ON v.id = p.vehicle_id
WHERE p.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
