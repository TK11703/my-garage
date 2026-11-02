DELETE FROM service_line_item_parts p
USING service_line_items li, service_records sr, vehicles v
WHERE p.line_item_id = @LineItemId
  AND li.id = p.line_item_id
  AND li.service_record_id = @RecordId
  AND sr.id = li.service_record_id
  AND sr.vehicle_id = @VehicleId
  AND v.id = sr.vehicle_id
  AND v.owner_id = @OwnerId;
