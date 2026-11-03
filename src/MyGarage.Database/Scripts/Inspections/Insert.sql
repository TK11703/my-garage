INSERT INTO inspections (id, vehicle_id, inspection_type, inspection_date, cost, next_due_date, created_at, updated_at, created_by, updated_by)
SELECT @Id, v.id, @InspectionType, @InspectionDate, @Cost, @NextDueDate, @Now, @Now, @OwnerId, @OwnerId
FROM vehicles v
WHERE v.id = @VehicleId
  AND v.owner_id = @OwnerId
RETURNING id, vehicle_id, inspection_type, inspection_date, cost, next_due_date, created_at, updated_at, created_by, updated_by;
