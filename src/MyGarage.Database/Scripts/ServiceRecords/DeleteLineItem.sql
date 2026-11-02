DELETE FROM service_line_items li
USING service_records sr, vehicles v
WHERE li.id = @LineItemId
  AND li.service_record_id = @RecordId
  AND sr.id = li.service_record_id
  AND sr.vehicle_id = @VehicleId
  AND v.id = sr.vehicle_id
  AND v.owner_id = @OwnerId;
