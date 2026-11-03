-- Locks the failed submission for this message id (if any) so only one resend can requeue it.
SELECT id
FROM email_submissions
WHERE message_id = @MessageId AND status = 'failed'
FOR UPDATE;
