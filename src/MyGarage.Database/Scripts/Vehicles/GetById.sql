SELECT id, owner_id, nickname, make, model, trim, color, mileage, doors, created_at, updated_at, created_by, updated_by
FROM vehicles
WHERE id = @Id
  AND owner_id = @OwnerId;
