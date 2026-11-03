SELECT id, submission_id, owner_id, file_name, content_type, size_bytes, sha256, created_at
FROM email_attachments
WHERE submission_id = @SubmissionId AND owner_id = @OwnerId
ORDER BY created_at, id;
