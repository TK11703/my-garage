SELECT sr.id, sr.vehicle_id, sr.date_serviced, sr.odometer_reading, sr.shop_or_person, sr.tax_amount,
       (SELECT count(*) FROM service_line_items li WHERE li.service_record_id = sr.id)::int AS line_item_count,
       (SELECT COALESCE(sum(li.total_cost), 0) FROM service_line_items li WHERE li.service_record_id = sr.id) + COALESCE(sr.tax_amount, 0) AS total_cost,
       sr.created_at, sr.updated_at, sr.created_by, sr.updated_by, sr.from_email
FROM service_records sr
JOIN vehicles v ON v.id = sr.vehicle_id
WHERE sr.id = @RecordId
  AND sr.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
