-- A final status. Anything other than ready leaves no draft to review, so the email content is purged with it.
UPDATE email_submissions
SET status = @Status,
    locked_until = NULL,
    failure_reason = NULL,
    sender = CASE WHEN @Status = 'ready' THEN sender END,
    subject = CASE WHEN @Status = 'ready' THEN subject END,
    body_text = CASE WHEN @Status = 'ready' THEN body_text END,
    skipped_attachments = CASE WHEN @Status = 'ready' THEN skipped_attachments ELSE '[]'::jsonb END,
    content_purged_at = CASE WHEN @Status = 'ready' THEN content_purged_at ELSE @Now END
WHERE id = @Id;
