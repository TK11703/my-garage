-- No row means the notification is not the owner's.
DELETE FROM notifications
WHERE id = @NotificationId
  AND owner_id = @OwnerId
RETURNING id;
