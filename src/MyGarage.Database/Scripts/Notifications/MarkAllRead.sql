UPDATE notifications
SET read_at = @Now
WHERE owner_id = @OwnerId
  AND read_at IS NULL;
