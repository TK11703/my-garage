-- Fuel and charging cost per calendar month, kind, and vehicle for the owner's vehicles, entries dated within [@From, @To].
-- Archived vehicles are included on purpose: the money was really spent.
SELECT
    date_trunc('month', fe.entry_date)::date AS month,
    fe.kind AS kind,
    v.id AS vehicle_id,
    v.year AS vehicle_year,
    v.make AS vehicle_make,
    v.model AS vehicle_model,
    v.archived_at IS NOT NULL AS vehicle_archived,
    sum(fe.total_cost) AS total
FROM fuel_energy_entries fe
JOIN vehicles v ON v.id = fe.vehicle_id
WHERE v.owner_id = @OwnerId
  AND fe.entry_date BETWEEN @From AND @To
GROUP BY 1, 2, v.id, v.year, v.make, v.model, v.archived_at IS NOT NULL
ORDER BY 1, 2, v.id;
