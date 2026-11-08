INSERT INTO reminders (id, vehicle_id, name, interval_miles, interval_months, last_done_on, last_done_mileage,
                       is_paused, notified_state, created_at, updated_at, created_by, updated_by)
SELECT @Id, v.id, @Name, @IntervalMiles, @IntervalMonths, @LastDoneOn, @LastDoneMileage,
       false, NULL, @Now, @Now, @OwnerId, @OwnerId
FROM vehicles v
WHERE v.id = @VehicleId
  AND v.owner_id = @OwnerId
RETURNING id;
