DELETE FROM service_records sr
USING vehicles v
WHERE sr.id = @RecordId
  AND sr.vehicle_id = @VehicleId
  AND v.id = sr.vehicle_id
  AND v.owner_id = @OwnerId;
