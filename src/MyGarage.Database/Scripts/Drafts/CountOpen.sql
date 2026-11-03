SELECT count(*)::int
FROM maintenance_drafts
WHERE owner_id = @OwnerId
  AND state IN ('needs_review', 'manual_entry');
