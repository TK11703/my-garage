DELETE FROM fuel_energy_entries fe
USING vehicles v
WHERE fe.id = @EntryId
  AND fe.vehicle_id = @VehicleId
  AND v.id = fe.vehicle_id
  AND v.owner_id = @OwnerId;
