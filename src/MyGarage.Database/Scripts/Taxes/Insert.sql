INSERT INTO taxes (id, vehicle_id, tax_type, amount, recurrence, created_at, updated_at, created_by, updated_by)
SELECT @Id, v.id, @TaxType, @Amount, @Recurrence, @Now, @Now, @OwnerId, @OwnerId
FROM vehicles v
WHERE v.id = @VehicleId
  AND v.owner_id = @OwnerId
RETURNING id, vehicle_id, tax_type, amount, recurrence, created_at, updated_at, created_by, updated_by;
