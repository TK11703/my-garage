-- One draft with its retained email details and attachment metadata; another owner's draft id finds nothing.
SELECT d.id, d.submission_id, d.attachment_id, d.state, d.kind, d.vehicle_id, d.vehicle_match, d.confidence,
       d.proposal::text AS proposal, d.notes, d.created_at, d.expires_at,
       s.arrived_at, s.sender, s.subject, s.origin,
       a.file_name AS attachment_file_name, a.content_type AS attachment_content_type,
       a.size_bytes AS attachment_size_bytes, a.sha256 AS attachment_sha256
FROM maintenance_drafts d
JOIN email_submissions s ON s.id = d.submission_id
LEFT JOIN email_attachments a ON a.id = d.attachment_id AND a.owner_id = d.owner_id
WHERE d.id = @DraftId
  AND d.owner_id = @OwnerId;
