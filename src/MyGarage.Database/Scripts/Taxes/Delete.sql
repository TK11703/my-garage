DELETE FROM taxes t
USING vehicles v
WHERE t.id = @EntryId
  AND t.vehicle_id = @VehicleId
  AND v.id = t.vehicle_id
  AND v.owner_id = @OwnerId;
