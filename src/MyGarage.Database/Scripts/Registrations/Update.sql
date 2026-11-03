UPDATE registrations r
SET state = @State,
    cost = @Cost,
    expiration_date = @ExpirationDate,
    updated_at = @Now,
    updated_by = @OwnerId
FROM vehicles v
WHERE r.id = @EntryId
  AND r.vehicle_id = @VehicleId
  AND v.id = r.vehicle_id
  AND v.owner_id = @OwnerId
RETURNING r.id, r.vehicle_id, r.state, r.cost, r.expiration_date, r.created_at, r.updated_at, r.created_by, r.updated_by;
