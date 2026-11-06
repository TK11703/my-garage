-- What became of the owner's emails, newest first. Emails still waiting to be read (queued, processing) and ones with no owner (not_accepted) are left out.
-- Outcome: not_applicable and failed as such; otherwise awaiting_review while any draft is open, else included, else discarded, else expired.
-- Records lists what the included drafts created, as JSON text; only vehicles that still exist have a link. No sender, subject, or text is selected.
-- @Outcome (optional) keeps only emails whose outcome is that value.
WITH history AS (
    SELECT s.id AS submission_id,
           s.arrived_at,
           s.origin,
           CASE
               WHEN s.status IN ('not_applicable', 'failed') THEN s.status
               WHEN EXISTS (SELECT 1 FROM maintenance_drafts d WHERE d.submission_id = s.id AND d.owner_id = @OwnerId AND d.state IN ('needs_review', 'manual_entry')) THEN 'awaiting_review'
               WHEN EXISTS (SELECT 1 FROM maintenance_drafts d WHERE d.submission_id = s.id AND d.owner_id = @OwnerId AND d.state = 'included') THEN 'included'
               WHEN EXISTS (SELECT 1 FROM maintenance_drafts d WHERE d.submission_id = s.id AND d.owner_id = @OwnerId AND d.state = 'discarded') THEN 'discarded'
               ELSE 'expired'
           END AS outcome
    FROM email_submissions s
    WHERE s.owner_id = @OwnerId
      AND s.status IN ('ready', 'not_applicable', 'failed')
)
SELECT h.submission_id,
       h.arrived_at,
       h.origin,
       h.outcome,
       COALESCE(
           (SELECT jsonb_agg(
                       jsonb_build_object('kind', d.created_record_kind, 'vehicleId', d.vehicle_id, 'recordId', d.created_record_id)
                       ORDER BY d.decided_at, d.id)
            FROM maintenance_drafts d
            JOIN vehicles v ON v.id = d.vehicle_id AND v.owner_id = @OwnerId
            WHERE d.submission_id = h.submission_id
              AND d.owner_id = @OwnerId
              AND d.state = 'included'
              AND d.created_record_kind IS NOT NULL
              AND d.created_record_id IS NOT NULL),
           '[]'::jsonb)::text AS records_json
FROM history h
WHERE @Outcome::text IS NULL OR h.outcome = @Outcome::text
ORDER BY h.arrived_at DESC, h.submission_id DESC
LIMIT @PageSize OFFSET @Offset;
