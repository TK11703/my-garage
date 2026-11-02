SELECT count(*)
FROM vehicles v
WHERE v.owner_id = @OwnerId
  AND (@Pattern::text IS NULL
       OR v.nickname ILIKE @Pattern ESCAPE '\'
       OR v.make ILIKE @Pattern ESCAPE '\'
       OR v.model ILIKE @Pattern ESCAPE '\'
       OR v.trim ILIKE @Pattern ESCAPE '\'
       OR v.color ILIKE @Pattern ESCAPE '\'
       OR v.year::text ILIKE @Pattern ESCAPE '\'
       OR v.vin ILIKE @Pattern ESCAPE '\'
       OR v.engine ILIKE @Pattern ESCAPE '\'
       OR EXISTS (SELECT 1 FROM vehicle_tags t WHERE t.vehicle_id = v.id AND t.name ILIKE @Pattern ESCAPE '\'))
  AND (@Make::text IS NULL OR lower(v.make) = lower(@Make))
  AND (@Color::text IS NULL OR lower(v.color) = lower(@Color))
  AND (@Doors::int IS NULL OR v.doors = @Doors)
  AND (@Tag::text IS NULL OR EXISTS (SELECT 1 FROM vehicle_tags t WHERE t.vehicle_id = v.id AND t.name_key = lower(@Tag)))
  AND (@MinMileage::int IS NULL OR v.mileage >= @MinMileage)
  AND (@MaxMileage::int IS NULL OR v.mileage <= @MaxMileage)
  AND (@MinYear::int IS NULL OR v.year >= @MinYear)
  AND (@MaxYear::int IS NULL OR v.year <= @MaxYear)
  AND (@VehicleTypeId::uuid IS NULL OR v.vehicle_type_id = @VehicleTypeId)
  AND (@FuelTypeId::uuid IS NULL OR v.fuel_type_id = @FuelTypeId)
  AND (@TransmissionId::uuid IS NULL OR v.transmission_id = @TransmissionId)
  AND (@DrivetrainId::uuid IS NULL OR v.drivetrain_id = @DrivetrainId);
