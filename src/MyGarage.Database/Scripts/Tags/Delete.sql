DELETE FROM vehicle_tags t
USING vehicles v
WHERE t.id = @TagId
  AND t.vehicle_id = @VehicleId
  AND v.id = t.vehicle_id
  AND v.owner_id = @OwnerId;
