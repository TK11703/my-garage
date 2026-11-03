-- Removes the email's sender, subject, and text once no draft of the submission is still waiting. OwnerId is null for the expiry job, which covers every owner.
UPDATE email_submissions s
SET sender = NULL,
    subject = NULL,
    body_text = NULL,
    skipped_attachments = '[]'::jsonb,
    content_purged_at = COALESCE(s.content_purged_at, @Now)
WHERE s.id = @SubmissionId
  AND (@OwnerId::text IS NULL OR s.owner_id = @OwnerId)
  AND NOT EXISTS (
      SELECT 1
      FROM maintenance_drafts d
      WHERE d.submission_id = s.id
        AND d.state IN ('needs_review', 'manual_entry'));
