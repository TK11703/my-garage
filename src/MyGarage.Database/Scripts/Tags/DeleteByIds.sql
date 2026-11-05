DELETE FROM vehicle_tags t
USING vehicles v
WHERE t.vehicle_id = @VehicleId
  AND v.id = t.vehicle_id
  AND v.owner_id = @OwnerId
  AND t.id = ANY(@TagIds);
