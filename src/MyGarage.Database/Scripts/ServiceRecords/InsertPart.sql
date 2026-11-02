INSERT INTO service_line_item_parts (id, line_item_id, position, name, quantity, unit_price)
SELECT @Id, li.id, @Position, @Name, @Quantity, @UnitPrice
FROM service_line_items li
JOIN service_records sr ON sr.id = li.service_record_id
JOIN vehicles v ON v.id = sr.vehicle_id
WHERE li.id = @LineItemId
  AND li.service_record_id = @RecordId
  AND sr.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
