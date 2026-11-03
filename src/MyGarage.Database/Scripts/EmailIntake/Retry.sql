-- Back to the queue for another attempt; only a reason code is kept, never email content.
UPDATE email_submissions
SET status = 'queued',
    locked_until = NULL,
    next_attempt_at = @NextAttemptAt,
    failure_reason = @Reason
WHERE id = @Id;
