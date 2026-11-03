-- The vehicle and attachment links are kept only when they belong to the owner (and, for the attachment, to this submission).
INSERT INTO maintenance_drafts (
    id, owner_id, submission_id, attachment_id, state, kind, vehicle_id, vehicle_match, confidence, proposal, notes, created_at, expires_at)
VALUES (
    @Id,
    @OwnerId,
    @SubmissionId,
    (SELECT a.id FROM email_attachments a WHERE a.id = @AttachmentId AND a.owner_id = @OwnerId AND a.submission_id = @SubmissionId),
    @State,
    @Kind,
    (SELECT v.id FROM vehicles v WHERE v.id = @VehicleId AND v.owner_id = @OwnerId),
    @VehicleMatch,
    @Confidence,
    @Proposal::jsonb,
    @Notes,
    @CreatedAt,
    @ExpiresAt);
