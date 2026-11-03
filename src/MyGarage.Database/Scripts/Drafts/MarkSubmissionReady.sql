-- The submission becomes ready only for its own owner; no row means another owner's id, and the caller writes nothing.
UPDATE email_submissions
SET status = 'ready', locked_until = NULL, failure_reason = NULL
WHERE id = @SubmissionId AND owner_id = @OwnerId
RETURNING id;
