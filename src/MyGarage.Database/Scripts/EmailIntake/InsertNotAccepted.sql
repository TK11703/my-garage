-- A sender that matched no single user: only the message id and times are kept, never the sender, subject, text, or attachments.
INSERT INTO email_submissions (id, message_id, status, arrived_at, next_attempt_at)
VALUES (@Id, @MessageId, 'not_accepted', @ArrivedAt, @ArrivedAt)
ON CONFLICT (message_id) DO NOTHING
RETURNING id;
