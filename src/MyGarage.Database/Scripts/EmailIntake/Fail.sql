-- Gives up on the submission: only a reason code is kept and the email content is purged (a repeated message id requeues it with new content).
UPDATE email_submissions
SET status = 'failed',
    locked_until = NULL,
    failure_reason = @Reason,
    sender = NULL,
    subject = NULL,
    body_text = NULL,
    skipped_attachments = '[]'::jsonb,
    content_purged_at = @Now
WHERE id = @Id;
