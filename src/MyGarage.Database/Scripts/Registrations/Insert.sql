INSERT INTO registrations (id, vehicle_id, state, cost, expiration_date, created_at, updated_at, created_by, updated_by, from_email)
SELECT @Id, v.id, @State, @Cost, @ExpirationDate, @Now, @Now, @OwnerId, @OwnerId, @FromEmail
FROM vehicles v
WHERE v.id = @VehicleId
  AND v.owner_id = @OwnerId
RETURNING id, vehicle_id, state, cost, expiration_date, created_at, updated_at, created_by, updated_by, from_email;
