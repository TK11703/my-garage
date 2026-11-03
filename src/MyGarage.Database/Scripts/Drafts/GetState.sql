SELECT state
FROM maintenance_drafts
WHERE id = @DraftId
  AND owner_id = @OwnerId;
