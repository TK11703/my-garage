SELECT c.id, c.reminder_id, c.done_on, c.mileage, c.created_at
FROM reminder_completions c
JOIN reminders r ON r.id = c.reminder_id
JOIN vehicles v ON v.id = r.vehicle_id
WHERE c.reminder_id = @ReminderId
  AND r.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId
ORDER BY c.done_on DESC, c.created_at DESC, c.id DESC;
