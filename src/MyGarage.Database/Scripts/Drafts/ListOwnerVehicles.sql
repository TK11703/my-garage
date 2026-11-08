SELECT id, year, make, model, vin
FROM vehicles
WHERE owner_id = @OwnerId
  AND archived_at IS NULL
ORDER BY created_at, id;
