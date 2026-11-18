SELECT v.id, v.owner_id, v.vin, v.make, v.model, v.trim, v.year,
       v.vehicle_type_id, vt.name AS vehicle_type_name,
       v.ownership, v.purchased_on, v.lease_ends_on, v.engine, v.horsepower,
       v.fuel_type_id, ft.name AS fuel_type_name,
       v.transmission_id, tr.name AS transmission_name,
       v.mileage, v.latest_odometer_reading, v.color, v.seats, v.doors,
       -- The garage mileage rule of Vehicles/ListPage.sql, so a vehicle shows one odometer figure everywhere.
       GREATEST(lr.odometer_reading, v.latest_odometer_reading, v.mileage) AS current_mileage, lr.date_serviced AS last_service_on,
       v.drivetrain_id, dt.name AS drivetrain_name,
       v.wheel_size, v.tire_size,
       v.created_at, v.updated_at, v.created_by, v.updated_by, v.archived_at
FROM vehicles v
LEFT JOIN lookup_items vt ON vt.id = v.vehicle_type_id
LEFT JOIN lookup_items ft ON ft.id = v.fuel_type_id
LEFT JOIN lookup_items tr ON tr.id = v.transmission_id
LEFT JOIN lookup_items dt ON dt.id = v.drivetrain_id
LEFT JOIN LATERAL (
    SELECT sr.odometer_reading, sr.date_serviced
    FROM service_records sr
    WHERE sr.vehicle_id = v.id
    ORDER BY sr.date_serviced DESC, sr.created_at DESC, sr.id DESC
    LIMIT 1) lr ON true
WHERE v.id = @Id
  AND v.owner_id = @OwnerId;
