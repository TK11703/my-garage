INSERT INTO insurance_policies (id, vehicle_id, start_date, end_date, duration, cost, created_at, updated_at, created_by, updated_by)
SELECT @Id, v.id, @StartDate, @EndDate, @Duration, @Cost, @Now, @Now, @OwnerId, @OwnerId
FROM vehicles v
WHERE v.id = @VehicleId
  AND v.owner_id = @OwnerId
RETURNING id, vehicle_id, start_date, end_date, duration, cost, created_at, updated_at, created_by, updated_by;
