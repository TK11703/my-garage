-- Services the owner's history shows more than once, with an interval taken from the gaps between them, for a reminder that does not exist yet.
-- Scoped to the owner's active vehicle (an archived vehicle suggests nothing). A service is a service line item description, compared ignoring case and surrounding spaces.
-- The distance interval is the median odometer gap rounded to the nearest 500 miles; the time interval is the median gap in whole months. Gaps of zero are ignored.
WITH owned AS (
    SELECT v.id
    FROM vehicles v
    WHERE v.id = @VehicleId
      AND v.owner_id = @OwnerId
      AND v.archived_at IS NULL
),
done AS (
    SELECT DISTINCT ON (lower(btrim(li.description)), sr.id)
           lower(btrim(li.description)) AS key,
           btrim(li.description) AS name,
           sr.date_serviced,
           sr.odometer_reading,
           sr.created_at
    FROM owned
    JOIN service_records sr ON sr.vehicle_id = owned.id
    JOIN service_line_items li ON li.service_record_id = sr.id
    WHERE btrim(li.description) <> ''
    ORDER BY lower(btrim(li.description)), sr.id, li.created_at, li.id
),
gaps AS (
    SELECT d.key, d.name, d.date_serviced, d.odometer_reading, d.created_at,
           d.date_serviced - lag(d.date_serviced) OVER w AS days_since,
           d.odometer_reading - lag(d.odometer_reading) OVER w AS miles_since
    FROM done d
    WINDOW w AS (PARTITION BY d.key ORDER BY d.date_serviced, d.created_at)
),
summary AS (
    SELECT g.key,
           (array_agg(g.name ORDER BY g.date_serviced DESC, g.created_at DESC))[1] AS name,
           count(*)::int AS times,
           max(g.date_serviced) AS last_done_on,
           (array_agg(g.odometer_reading ORDER BY g.date_serviced DESC, g.created_at DESC))[1] AS last_done_mileage,
           percentile_cont(0.5) WITHIN GROUP (ORDER BY g.days_since) FILTER (WHERE g.days_since > 0) AS median_days,
           percentile_cont(0.5) WITHIN GROUP (ORDER BY g.miles_since) FILTER (WHERE g.miles_since > 0) AS median_miles
    FROM gaps g
    GROUP BY g.key
    HAVING count(*) >= 2
)
SELECT s.name,
       CASE WHEN s.median_miles IS NULL THEN NULL
            ELSE LEAST(999999, GREATEST(500, round(s.median_miles / 500.0)::int * 500)) END AS interval_miles,
       CASE WHEN s.median_days IS NULL THEN NULL
            ELSE LEAST(600, GREATEST(1, round(s.median_days / 30.4375)::int)) END AS interval_months,
       s.last_done_on,
       s.last_done_mileage,
       s.times
FROM summary s
WHERE (s.median_miles IS NOT NULL OR s.median_days IS NOT NULL)
  AND NOT EXISTS (
      SELECT 1
      FROM reminders r
      WHERE r.vehicle_id = @VehicleId
        AND lower(btrim(r.name)) = s.key)
ORDER BY s.last_done_on DESC, s.name
LIMIT 5;
