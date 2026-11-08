SELECT n.id, n.owner_id, n.kind, n.draft_id, n.reminder_id, r.vehicle_id, n.title, n.created_at, n.read_at
FROM notifications n
LEFT JOIN reminders r ON r.id = n.reminder_id
WHERE n.owner_id = @OwnerId
  AND (NOT @UnreadOnly OR n.read_at IS NULL)
ORDER BY n.created_at DESC, n.id DESC
LIMIT @Take;
