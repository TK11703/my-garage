INSERT INTO journal_notes (id, vehicle_id, category, body, created_at, updated_at, created_by, updated_by)
SELECT @Id, v.id, @Category, @Body, @Now, @Now, @OwnerId, @OwnerId
FROM vehicles v
WHERE v.id = @VehicleId
  AND v.owner_id = @OwnerId
RETURNING id, category, body, created_at, updated_at, created_by, updated_by;
