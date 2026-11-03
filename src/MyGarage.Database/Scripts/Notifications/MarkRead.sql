-- An already read notification keeps its earlier read time; no row means the notification is not the owner's.
UPDATE notifications
SET read_at = COALESCE(read_at, @Now)
WHERE id = @NotificationId
  AND owner_id = @OwnerId
RETURNING id;
