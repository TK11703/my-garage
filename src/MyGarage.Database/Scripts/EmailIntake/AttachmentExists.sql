-- A system sweep, not an owner request: it asks only whether any row (of any owner) still points at the stored file.
SELECT EXISTS (
    SELECT 1
    FROM email_attachments
    WHERE id = @AttachmentId AND submission_id = @SubmissionId);
