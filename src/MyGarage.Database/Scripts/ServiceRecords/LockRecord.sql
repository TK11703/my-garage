SELECT sr.id
FROM service_records sr
JOIN vehicles v ON v.id = sr.vehicle_id
WHERE sr.id = @RecordId
  AND sr.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId
FOR UPDATE OF sr;
