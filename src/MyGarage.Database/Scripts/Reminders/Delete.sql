-- Completions and notifications go with it by cascade.
DELETE FROM reminders r
USING vehicles v
WHERE v.id = r.vehicle_id
  AND r.id = @ReminderId
  AND r.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId
RETURNING r.id;
