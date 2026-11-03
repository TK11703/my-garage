UPDATE notifications
SET read_at = COALESCE(read_at, @Now)
WHERE draft_id = @DraftId
  AND owner_id = @OwnerId;
