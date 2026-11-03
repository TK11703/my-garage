SELECT i.id, i.vehicle_id, i.inspection_type, i.inspection_date, i.cost, i.next_due_date, i.created_at, i.updated_at, i.created_by, i.updated_by, i.from_email
FROM inspections i
JOIN vehicles v ON v.id = i.vehicle_id
WHERE i.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId
ORDER BY i.inspection_date DESC, i.created_at DESC, i.id DESC
LIMIT @PageSize OFFSET @Offset;
