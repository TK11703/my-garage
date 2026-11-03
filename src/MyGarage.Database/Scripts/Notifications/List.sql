SELECT id, owner_id, kind, draft_id, title, created_at, read_at
FROM notifications
WHERE owner_id = @OwnerId
  AND (NOT @UnreadOnly OR read_at IS NULL)
ORDER BY created_at DESC, id DESC
LIMIT @Take;
