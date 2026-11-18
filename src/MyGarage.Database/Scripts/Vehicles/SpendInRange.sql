-- Money spent per vehicle on dated records in [@From, @To]: service records (line items plus tax), fuel and charging, and inspections.
-- @VehicleId limits it to one vehicle; null means all of the owner's. Archived vehicles are included on purpose: the money was really spent. Registration, insurance, and tax rows carry no payment date, so they are not counted.
SELECT s.vehicle_id, sum(s.amount) AS amount
FROM (
    SELECT sr.vehicle_id,
           (SELECT COALESCE(sum(li.total_cost), 0) FROM service_line_items li WHERE li.service_record_id = sr.id) + COALESCE(sr.tax_amount, 0) AS amount
    FROM service_records sr
    WHERE sr.date_serviced BETWEEN @From AND @To
    UNION ALL
    SELECT fe.vehicle_id, fe.total_cost
    FROM fuel_energy_entries fe
    WHERE fe.entry_date BETWEEN @From AND @To
    UNION ALL
    SELECT i.vehicle_id, i.cost
    FROM inspections i
    WHERE i.inspection_date BETWEEN @From AND @To
) s
JOIN vehicles v ON v.id = s.vehicle_id
WHERE v.owner_id = @OwnerId
  AND (@VehicleId::uuid IS NULL OR v.id = @VehicleId)
GROUP BY s.vehicle_id
HAVING sum(s.amount) <> 0;
