-- Leases the oldest due submission: a queued one that is due, or a processing one whose lease expired.
-- SKIP LOCKED means two workers (or two instances) never receive the same row.
WITH next AS (
    SELECT id
    FROM email_submissions
    WHERE (status = 'queued' AND next_attempt_at <= @Now)
       OR (status = 'processing' AND locked_until <= @Now)
    ORDER BY next_attempt_at, id
    LIMIT 1
    FOR UPDATE SKIP LOCKED
)
UPDATE email_submissions s
SET status = 'processing',
    locked_until = @LockedUntil,
    attempt_count = s.attempt_count + 1
FROM next
WHERE s.id = next.id
RETURNING
    s.id, s.message_id, s.status, s.arrived_at, s.received_at, s.owner_id, s.sender, s.subject, s.body_text,
    s.attempt_count, s.next_attempt_at, s.locked_until, s.failure_reason, s.skipped_attachments::text AS skipped_attachments, s.content_purged_at,
    s.origin, s.preselected_vehicle_id;
