DELETE FROM notifications
WHERE owner_id = @OwnerId
  AND read_at IS NOT NULL;
