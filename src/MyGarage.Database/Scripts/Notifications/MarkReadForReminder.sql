-- Marking a reminder done, pausing it, or moving it back to Upcoming reads its alerts.
UPDATE notifications
SET read_at = COALESCE(read_at, @Now)
WHERE reminder_id = @ReminderId
  AND owner_id = @OwnerId;
