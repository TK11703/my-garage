-- A vehicle's history across record kinds, newest first, for the owner's vehicle only (archived vehicles stay readable).
-- A registration, insurance policy, or tax has no event date, so it sits at the day it was added (insurance: its start date).
-- @Kind is null for every kind, or one of service, fuel-energy, inspection, registration, insurance, tax, journal. Keep Vehicles/TimelineCount.sql in step.
WITH entries AS (
    SELECT 'service'::text AS kind, sr.id, sr.date_serviced AS occurred_on, sr.created_at,
           COALESCE((SELECT string_agg(li.description, ', ' ORDER BY li.created_at, li.id) FROM service_line_items li WHERE li.service_record_id = sr.id), 'Service record') AS title,
           sr.shop_or_person AS detail,
           (SELECT COALESCE(sum(li.total_cost), 0) FROM service_line_items li WHERE li.service_record_id = sr.id) + COALESCE(sr.tax_amount, 0) AS amount,
           sr.odometer_reading, sr.from_email
    FROM service_records sr
    WHERE sr.vehicle_id = @VehicleId AND (@Kind::text IS NULL OR @Kind = 'service')
    UNION ALL
    SELECT 'fuel-energy', fe.id, fe.entry_date, fe.created_at,
           CASE fe.kind WHEN 'fuel' THEN 'Fuel' ELSE 'Charge' END,
           trim_scale(fe.quantity)::text || CASE fe.kind WHEN 'fuel' THEN ' gal' ELSE ' kWh' END,
           fe.total_cost, NULL, fe.from_email
    FROM fuel_energy_entries fe
    WHERE fe.vehicle_id = @VehicleId AND (@Kind::text IS NULL OR @Kind = 'fuel-energy')
    UNION ALL
    SELECT 'inspection', i.id, i.inspection_date, i.created_at,
           initcap(i.inspection_type) || ' inspection',
           CASE WHEN i.next_due_date IS NOT NULL THEN 'Next due ' || to_char(i.next_due_date, 'Mon FMDD, YYYY') END,
           i.cost, NULL, i.from_email
    FROM inspections i
    WHERE i.vehicle_id = @VehicleId AND (@Kind::text IS NULL OR @Kind = 'inspection')
    UNION ALL
    SELECT 'registration', r.id, (r.created_at AT TIME ZONE 'UTC')::date, r.created_at,
           'Registration ' || r.state,
           'Expires ' || to_char(r.expiration_date, 'Mon FMDD, YYYY'),
           r.cost, NULL, r.from_email
    FROM registrations r
    WHERE r.vehicle_id = @VehicleId AND (@Kind::text IS NULL OR @Kind = 'registration')
    UNION ALL
    SELECT 'insurance', p.id, p.start_date, p.created_at,
           'Insurance',
           'Through ' || to_char(p.end_date, 'Mon FMDD, YYYY'),
           p.cost, NULL, p.from_email
    FROM insurance_policies p
    WHERE p.vehicle_id = @VehicleId AND (@Kind::text IS NULL OR @Kind = 'insurance')
    UNION ALL
    SELECT 'tax', t.id, (t.created_at AT TIME ZONE 'UTC')::date, t.created_at,
           initcap(replace(t.tax_type, '_', ' ')) || ' tax',
           initcap(replace(t.recurrence, '_', '-')),
           t.amount, NULL, t.from_email
    FROM taxes t
    WHERE t.vehicle_id = @VehicleId AND (@Kind::text IS NULL OR @Kind = 'tax')
    UNION ALL
    SELECT 'journal', n.id, (n.created_at AT TIME ZONE 'UTC')::date, n.created_at,
           COALESCE(n.category, 'Journal note'),
           left(n.body, 140),
           NULL, NULL, false
    FROM journal_notes n
    WHERE n.vehicle_id = @VehicleId AND (@Kind::text IS NULL OR @Kind = 'journal')
)
SELECT e.kind, e.id, e.occurred_on, e.title, e.detail, e.amount, e.odometer_reading, e.from_email
FROM entries e
JOIN vehicles v ON v.id = @VehicleId
WHERE v.owner_id = @OwnerId
ORDER BY e.occurred_on DESC, e.created_at DESC, e.id DESC
LIMIT @PageSize OFFSET @Offset;
