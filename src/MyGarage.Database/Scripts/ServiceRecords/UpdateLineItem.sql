UPDATE service_line_items li
SET description = @Description,
    flat_cost   = @FlatCost,
    labor_hours = @LaborHours,
    labor_rate  = @LaborRate,
    total_cost  = @TotalCost,
    updated_at  = @Now,
    updated_by  = @OwnerId
FROM service_records sr
JOIN vehicles v ON v.id = sr.vehicle_id
WHERE li.id = @LineItemId
  AND li.service_record_id = @RecordId
  AND sr.id = li.service_record_id
  AND sr.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
