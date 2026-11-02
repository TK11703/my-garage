SELECT count(*)
FROM vehicles v
-- The latest-dated service record, as in ListPage.sql, so the mileage filters match the card.
LEFT JOIN LATERAL (
    SELECT sr.odometer_reading
    FROM service_records sr
    WHERE sr.vehicle_id = v.id
    ORDER BY sr.date_serviced DESC, sr.created_at DESC, sr.id DESC
    LIMIT 1) lr ON true
WHERE v.owner_id = @OwnerId
  AND (@Pattern::text IS NULL
       OR concat_ws(' ', v.year::text, v.make, v.model) ILIKE @Pattern ESCAPE '\'
       OR v.make ILIKE @Pattern ESCAPE '\'
       OR v.model ILIKE @Pattern ESCAPE '\'
       OR v.trim ILIKE @Pattern ESCAPE '\'
       OR v.color ILIKE @Pattern ESCAPE '\'
       OR v.year::text ILIKE @Pattern ESCAPE '\'
       OR v.vin ILIKE @Pattern ESCAPE '\'
       OR v.engine ILIKE @Pattern ESCAPE '\'
       OR EXISTS (SELECT 1 FROM vehicle_tags t JOIN features f ON f.id = t.feature_id WHERE t.vehicle_id = v.id AND f.name ILIKE @Pattern ESCAPE '\'))
  AND (@Make::text IS NULL OR lower(v.make) = lower(@Make))
  AND (@Color::text IS NULL OR lower(v.color) = lower(@Color))
  AND (@Doors::int IS NULL OR v.doors = @Doors)
  AND (@Tag::text IS NULL OR EXISTS (SELECT 1 FROM vehicle_tags t JOIN features f ON f.id = t.feature_id WHERE t.vehicle_id = v.id AND f.name_key = lower(@Tag)))
  AND (@MinMileage::int IS NULL OR GREATEST(lr.odometer_reading, v.latest_odometer_reading, v.mileage) >= @MinMileage)
  AND (@MaxMileage::int IS NULL OR GREATEST(lr.odometer_reading, v.latest_odometer_reading, v.mileage) <= @MaxMileage)
  AND (@MinYear::int IS NULL OR v.year >= @MinYear)
  AND (@MaxYear::int IS NULL OR v.year <= @MaxYear)
  AND (@VehicleTypeId::uuid IS NULL OR v.vehicle_type_id = @VehicleTypeId)
  AND (@FuelTypeId::uuid IS NULL OR v.fuel_type_id = @FuelTypeId)
  AND (@TransmissionId::uuid IS NULL OR v.transmission_id = @TransmissionId)
  AND (@DrivetrainId::uuid IS NULL OR v.drivetrain_id = @DrivetrainId);
