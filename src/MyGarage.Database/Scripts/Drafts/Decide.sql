-- The guarded decision: no row means the draft is not the owner's or is already decided. The vehicle link is kept only when the owner owns it.
UPDATE maintenance_drafts
SET state = @NewState,
    decided_at = @Now,
    proposal = '{}'::jsonb,
    vehicle_id = COALESCE((SELECT v.id FROM vehicles v WHERE v.id = @VehicleId::uuid AND v.owner_id = @OwnerId), vehicle_id),
    created_record_kind = @CreatedRecordKind,
    created_record_id = @CreatedRecordId::uuid
WHERE id = @DraftId
  AND owner_id = @OwnerId
  AND state IN ('needs_review', 'manual_entry')
RETURNING submission_id, attachment_id;
