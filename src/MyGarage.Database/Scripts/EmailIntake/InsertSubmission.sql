-- A repeated message id changes nothing, except that a failed submission is queued again with the new content (R18).
-- The id changes with it so the new blobs ({submissionId}/{attachmentId}) match the row; its old attachments are removed first by the caller's transaction.
INSERT INTO email_submissions (
    id, message_id, status, arrived_at, received_at, owner_id, sender, subject, body_text, attempt_count, next_attempt_at, skipped_attachments)
VALUES (
    @Id, @MessageId, 'queued', @ArrivedAt, @ReceivedAt, @OwnerId, @Sender, @Subject, @BodyText, 0, @ArrivedAt, @SkippedAttachments::jsonb)
ON CONFLICT (message_id) DO UPDATE
SET id = EXCLUDED.id,
    status = 'queued',
    arrived_at = EXCLUDED.arrived_at,
    received_at = EXCLUDED.received_at,
    owner_id = EXCLUDED.owner_id,
    sender = EXCLUDED.sender,
    subject = EXCLUDED.subject,
    body_text = EXCLUDED.body_text,
    attempt_count = 0,
    next_attempt_at = EXCLUDED.next_attempt_at,
    locked_until = NULL,
    failure_reason = NULL,
    skipped_attachments = EXCLUDED.skipped_attachments,
    content_purged_at = NULL
WHERE email_submissions.status = 'failed'
RETURNING id, (xmax = 0) AS inserted;
