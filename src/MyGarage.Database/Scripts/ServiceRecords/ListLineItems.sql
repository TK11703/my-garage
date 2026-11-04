SELECT li.id, li.service_record_id, li.description, li.flat_cost, li.labor_hours, li.labor_rate, li.labor_cost, li.total_cost
FROM service_line_items li
JOIN service_records sr ON sr.id = li.service_record_id
JOIN vehicles v ON v.id = sr.vehicle_id
WHERE li.service_record_id = @RecordId
  AND sr.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId
ORDER BY li.created_at, li.id;
