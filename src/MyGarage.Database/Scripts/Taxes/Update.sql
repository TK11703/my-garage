UPDATE taxes t
SET tax_type = @TaxType,
    amount = @Amount,
    recurrence = @Recurrence,
    updated_at = @Now,
    updated_by = @OwnerId
FROM vehicles v
WHERE t.id = @EntryId
  AND t.vehicle_id = @VehicleId
  AND v.id = t.vehicle_id
  AND v.owner_id = @OwnerId
RETURNING t.id, t.vehicle_id, t.tax_type, t.amount, t.recurrence, t.created_at, t.updated_at, t.created_by, t.updated_by, t.from_email;
