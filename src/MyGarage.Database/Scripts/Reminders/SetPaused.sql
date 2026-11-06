UPDATE reminders r
SET is_paused = @Paused,
    updated_at = @Now,
    updated_by = @OwnerId
FROM vehicles v
WHERE v.id = r.vehicle_id
  AND r.id = @ReminderId
  AND r.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId
RETURNING r.id;
