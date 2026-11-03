SELECT p.id, p.vehicle_id, p.start_date, p.end_date, p.duration, p.cost, p.created_at, p.updated_at, p.created_by, p.updated_by, p.from_email
FROM insurance_policies p
JOIN vehicles v ON v.id = p.vehicle_id
WHERE p.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId
ORDER BY p.end_date DESC, p.created_at DESC, p.id DESC
LIMIT @PageSize OFFSET @Offset;
