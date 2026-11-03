SELECT count(*)::int
FROM email_submissions
WHERE owner_id = @OwnerId
  AND status IN ('ready', 'not_applicable', 'failed');
