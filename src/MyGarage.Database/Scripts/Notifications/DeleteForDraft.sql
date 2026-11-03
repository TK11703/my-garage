-- A decided or expired draft no longer needs its notification.
DELETE FROM notifications
WHERE draft_id = @DraftId
  AND owner_id = @OwnerId;
