SELECT r.id, r.vehicle_id, r.state, r.cost, r.expiration_date, r.created_at, r.updated_at, r.created_by, r.updated_by
FROM registrations r
JOIN vehicles v ON v.id = r.vehicle_id
WHERE r.id = @EntryId
  AND r.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
