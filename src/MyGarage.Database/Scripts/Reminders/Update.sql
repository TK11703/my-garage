UPDATE reminders r
SET name = @Name,
    applies_to = @AppliesTo,
    subject = @Subject,
    interval_miles = @IntervalMiles,
    interval_months = @IntervalMonths,
    last_done_on = @LastDoneOn,
    last_done_mileage = @LastDoneMileage,
    updated_at = @Now,
    updated_by = @OwnerId
FROM vehicles v
WHERE v.id = r.vehicle_id
  AND r.id = @ReminderId
  AND r.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId
RETURNING r.id;
