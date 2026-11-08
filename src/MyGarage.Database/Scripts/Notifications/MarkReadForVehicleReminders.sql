-- A vehicle's waiting reminder notices are marked read when it is archived (they would only point at a disabled reminder).
UPDATE notifications n
SET read_at = @Now
FROM reminders r
JOIN vehicles v ON v.id = r.vehicle_id
WHERE n.reminder_id = r.id
  AND r.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId
  AND n.read_at IS NULL;
