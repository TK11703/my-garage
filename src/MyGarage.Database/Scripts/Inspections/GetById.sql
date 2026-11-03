SELECT i.id, i.vehicle_id, i.inspection_type, i.inspection_date, i.cost, i.next_due_date, i.created_at, i.updated_at, i.created_by, i.updated_by
FROM inspections i
JOIN vehicles v ON v.id = i.vehicle_id
WHERE i.id = @EntryId
  AND i.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
