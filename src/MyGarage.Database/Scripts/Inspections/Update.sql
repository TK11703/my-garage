UPDATE inspections i
SET inspection_type = @InspectionType,
    inspection_date = @InspectionDate,
    cost = @Cost,
    next_due_date = @NextDueDate,
    updated_at = @Now,
    updated_by = @OwnerId
FROM vehicles v
WHERE i.id = @EntryId
  AND i.vehicle_id = @VehicleId
  AND v.id = i.vehicle_id
  AND v.owner_id = @OwnerId
RETURNING i.id, i.vehicle_id, i.inspection_type, i.inspection_date, i.cost, i.next_due_date, i.created_at, i.updated_at, i.created_by, i.updated_by;
