DELETE FROM inspections i
USING vehicles v
WHERE i.id = @EntryId
  AND i.vehicle_id = @VehicleId
  AND v.id = i.vehicle_id
  AND v.owner_id = @OwnerId;
