-- Re-presents an undecided draft as another record kind; a manual_entry draft becomes needs_review.
UPDATE maintenance_drafts
SET kind = @Kind,
    proposal = @Proposal::jsonb,
    state = 'needs_review'
WHERE id = @DraftId
  AND owner_id = @OwnerId
  AND state IN ('needs_review', 'manual_entry')
RETURNING id;
