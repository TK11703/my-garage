SELECT t.id, t.vehicle_id, t.tax_type, t.amount, t.recurrence, t.created_at, t.updated_at, t.created_by, t.updated_by, t.from_email
FROM taxes t
JOIN vehicles v ON v.id = t.vehicle_id
WHERE t.id = @EntryId
  AND t.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
