DELETE FROM insurance_policies p
USING vehicles v
WHERE p.id = @EntryId
  AND p.vehicle_id = @VehicleId
  AND v.id = p.vehicle_id
  AND v.owner_id = @OwnerId;
