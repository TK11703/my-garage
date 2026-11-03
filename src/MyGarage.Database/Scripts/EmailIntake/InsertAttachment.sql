INSERT INTO email_attachments (id, submission_id, owner_id, file_name, content_type, size_bytes, sha256, created_at)
VALUES (@Id, @SubmissionId, @OwnerId, @FileName, @ContentType, @SizeBytes, @Sha256, @CreatedAt);
