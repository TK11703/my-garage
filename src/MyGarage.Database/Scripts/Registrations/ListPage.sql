SELECT r.id, r.vehicle_id, r.state, r.cost, r.expiration_date, r.created_at, r.updated_at, r.created_by, r.updated_by
FROM registrations r
JOIN vehicles v ON v.id = r.vehicle_id
WHERE r.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId
ORDER BY r.expiration_date DESC, r.created_at DESC, r.id DESC
LIMIT @PageSize OFFSET @Offset;
