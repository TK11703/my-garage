SELECT v.id, v.year, v.make, v.model, v.mileage, mi.id AS main_image_id,
       ft.name AS fuel_type_name, tr.name AS transmission_name, v.ownership, v.purchased_on
FROM vehicles v
LEFT JOIN lookup_items ft ON ft.id = v.fuel_type_id
LEFT JOIN lookup_items tr ON tr.id = v.transmission_id
LEFT JOIN vehicle_images mi ON mi.vehicle_id = v.id AND mi.is_main
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
  AND (@MinMileage::int IS NULL OR v.mileage >= @MinMileage)
  AND (@MaxMileage::int IS NULL OR v.mileage <= @MaxMileage)
  AND (@MinYear::int IS NULL OR v.year >= @MinYear)
  AND (@MaxYear::int IS NULL OR v.year <= @MaxYear)
  AND (@VehicleTypeId::uuid IS NULL OR v.vehicle_type_id = @VehicleTypeId)
  AND (@FuelTypeId::uuid IS NULL OR v.fuel_type_id = @FuelTypeId)
  AND (@TransmissionId::uuid IS NULL OR v.transmission_id = @TransmissionId)
  AND (@DrivetrainId::uuid IS NULL OR v.drivetrain_id = @DrivetrainId)
ORDER BY lower(v.make), lower(v.model), v.year, v.id
LIMIT @PageSize OFFSET @Offset;
