INSERT INTO fuel_energy_entries (id, vehicle_id, kind, entry_date, unit_price, quantity, total_cost, created_at, updated_at, created_by, updated_by, from_email)
SELECT @Id, v.id, @Kind, @EntryDate, @UnitPrice, @Quantity, @TotalCost, @Now, @Now, @OwnerId, @OwnerId, @FromEmail
FROM vehicles v
WHERE v.id = @VehicleId
  AND v.owner_id = @OwnerId
RETURNING id, vehicle_id, kind, entry_date, unit_price, quantity, total_cost, created_at, updated_at, created_by, updated_by, from_email;
