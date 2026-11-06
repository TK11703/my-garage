-- Fuel and charging cost per calendar month and kind across the owner's vehicles, entries dated within [@From, @To].
SELECT
    date_trunc('month', fe.entry_date)::date AS month,
    fe.kind AS kind,
    sum(fe.total_cost) AS total
FROM fuel_energy_entries fe
JOIN vehicles v ON v.id = fe.vehicle_id
WHERE v.owner_id = @OwnerId
  AND fe.entry_date BETWEEN @From AND @To
GROUP BY 1, 2
ORDER BY 1, 2;
