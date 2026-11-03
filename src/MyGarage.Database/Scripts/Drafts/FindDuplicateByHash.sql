-- Another draft of the same owner, not discarded, that came from a file with the same SHA-256.
SELECT EXISTS (
    SELECT 1
    FROM maintenance_drafts d
    JOIN email_attachments a ON a.id = d.attachment_id AND a.owner_id = d.owner_id
    WHERE d.owner_id = @OwnerId
      AND d.id <> @DraftId
      AND d.state <> 'discarded'
      AND a.sha256 = @Sha256);
