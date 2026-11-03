-- Removes the attachment rows of a submission that is being requeued; the caller deletes the blobs of the returned ids after commit.
DELETE FROM email_attachments
WHERE submission_id = @SubmissionId
RETURNING id;
