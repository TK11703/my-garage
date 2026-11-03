DELETE FROM registrations r
USING vehicles v
WHERE r.id = @EntryId
  AND r.vehicle_id = @VehicleId
  AND v.id = r.vehicle_id
  AND v.owner_id = @OwnerId;
