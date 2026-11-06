UPDATE reminders r
SET last_done_on = @DoneOn,
    last_done_mileage = COALESCE(@Mileage, r.last_done_mileage),
    notified_state = NULL,
    updated_at = @Now,
    updated_by = @OwnerId
FROM vehicles v
WHERE v.id = r.vehicle_id
  AND r.id = @ReminderId
  AND r.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId
RETURNING r.id;
