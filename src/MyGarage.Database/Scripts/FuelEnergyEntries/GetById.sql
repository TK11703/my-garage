SELECT fe.id, fe.vehicle_id, fe.kind, fe.entry_date, fe.unit_price, fe.quantity, fe.total_cost, fe.created_at, fe.updated_at, fe.created_by, fe.updated_by
FROM fuel_energy_entries fe
JOIN vehicles v ON v.id = fe.vehicle_id
WHERE fe.id = @EntryId
  AND fe.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
