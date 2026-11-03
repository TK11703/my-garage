-- Expires every undecided draft that is due, for all owners (the hourly job); the proposal is cleared with it.
UPDATE maintenance_drafts
SET state = 'expired',
    decided_at = @Now,
    proposal = '{}'::jsonb
WHERE state IN ('needs_review', 'manual_entry')
  AND expires_at <= @Now
RETURNING id, owner_id, submission_id, attachment_id;
