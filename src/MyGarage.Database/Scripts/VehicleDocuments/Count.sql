SELECT count(*)::int
FROM vehicle_documents d
JOIN vehicles v ON v.id = d.vehicle_id
WHERE d.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
