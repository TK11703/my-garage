UPDATE fuel_energy_entries fe
SET kind = @Kind,
    entry_date = @EntryDate,
    unit_price = @UnitPrice,
    quantity = @Quantity,
    total_cost = @TotalCost,
    updated_at = @Now,
    updated_by = @OwnerId
FROM vehicles v
WHERE fe.id = @EntryId
  AND fe.vehicle_id = @VehicleId
  AND v.id = fe.vehicle_id
  AND v.owner_id = @OwnerId
RETURNING fe.id, fe.vehicle_id, fe.kind, fe.entry_date, fe.unit_price, fe.quantity, fe.total_cost, fe.created_at, fe.updated_at, fe.created_by, fe.updated_by, fe.from_email;
