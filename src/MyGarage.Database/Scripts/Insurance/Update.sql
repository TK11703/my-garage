UPDATE insurance_policies p
SET start_date = @StartDate,
    end_date = @EndDate,
    duration = @Duration,
    cost = @Cost,
    updated_at = @Now,
    updated_by = @OwnerId
FROM vehicles v
WHERE p.id = @EntryId
  AND p.vehicle_id = @VehicleId
  AND v.id = p.vehicle_id
  AND v.owner_id = @OwnerId
RETURNING p.id, p.vehicle_id, p.start_date, p.end_date, p.duration, p.cost, p.created_at, p.updated_at, p.created_by, p.updated_by, p.from_email;
