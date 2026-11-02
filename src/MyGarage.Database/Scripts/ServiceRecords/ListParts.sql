SELECT p.line_item_id, p.position, p.name, p.quantity, p.unit_price
FROM service_line_item_parts p
JOIN service_line_items li ON li.id = p.line_item_id
JOIN service_records sr ON sr.id = li.service_record_id
JOIN vehicles v ON v.id = sr.vehicle_id
WHERE li.service_record_id = @RecordId
  AND sr.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId
ORDER BY li.created_at, li.id, p.position;
