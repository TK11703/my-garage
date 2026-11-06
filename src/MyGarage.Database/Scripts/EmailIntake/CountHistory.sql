-- How many history rows the owner has, optionally only those whose outcome is @Outcome (the same outcome rule as ListHistoryPage).
SELECT count(*)::int
FROM (
    SELECT CASE
               WHEN s.status IN ('not_applicable', 'failed') THEN s.status
               WHEN EXISTS (SELECT 1 FROM maintenance_drafts d WHERE d.submission_id = s.id AND d.owner_id = @OwnerId AND d.state IN ('needs_review', 'manual_entry')) THEN 'awaiting_review'
               WHEN EXISTS (SELECT 1 FROM maintenance_drafts d WHERE d.submission_id = s.id AND d.owner_id = @OwnerId AND d.state = 'included') THEN 'included'
               WHEN EXISTS (SELECT 1 FROM maintenance_drafts d WHERE d.submission_id = s.id AND d.owner_id = @OwnerId AND d.state = 'discarded') THEN 'discarded'
               ELSE 'expired'
           END AS outcome
    FROM email_submissions s
    WHERE s.owner_id = @OwnerId
      AND s.status IN ('ready', 'not_applicable', 'failed')
) h
WHERE @Outcome::text IS NULL OR h.outcome = @Outcome::text;
