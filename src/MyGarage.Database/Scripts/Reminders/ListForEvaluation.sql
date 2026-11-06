SELECT r.id, r.vehicle_id, v.owner_id, v.year AS vehicle_year, v.make AS vehicle_make, v.model AS vehicle_model,
       r.name, r.applies_to, r.subject, r.interval_miles, r.interval_months, r.last_done_on, r.last_done_mileage,
       r.is_paused, r.notified_state,
       GREATEST(lr.odometer_reading, v.latest_odometer_reading, v.mileage) AS current_mileage,
       r.created_at, r.updated_at
FROM reminders r
JOIN vehicles v ON v.id = r.vehicle_id
-- The garage mileage rule of Vehicles/ListPage.sql: the latest-dated service record, the latest odometer reading, and the purchase mileage.
LEFT JOIN LATERAL (
    SELECT sr.odometer_reading
    FROM service_records sr
    WHERE sr.vehicle_id = v.id
    ORDER BY sr.date_serviced DESC, sr.created_at DESC, sr.id DESC
    LIMIT 1) lr ON true
WHERE NOT r.is_paused
  AND (@OwnerId::text IS NULL OR v.owner_id = @OwnerId)
  AND (@VehicleId::uuid IS NULL OR v.id = @VehicleId)
ORDER BY r.created_at, r.id;
