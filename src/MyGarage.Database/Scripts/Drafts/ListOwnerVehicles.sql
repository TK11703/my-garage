SELECT id, year, make, model, vin
FROM vehicles
WHERE owner_id = @OwnerId
ORDER BY created_at, id;
