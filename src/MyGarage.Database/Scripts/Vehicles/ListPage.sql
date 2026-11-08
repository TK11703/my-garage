SELECT v.id, v.year, v.make, v.model,
       GREATEST(lr.odometer_reading, v.latest_odometer_reading, v.mileage) AS mileage,
       mi.id AS main_image_id,
       ft.name AS fuel_type_name, tr.name AS transmission_name, v.ownership, v.purchased_on, v.archived_at,
       -- The other fields the search text was found in (name parts are already shown), for the search results page.
       CASE WHEN @Pattern::text IS NULL THEN '[]' ELSE (
           SELECT COALESCE(jsonb_agg(jsonb_build_object('field', m.field, 'text', m.text) ORDER BY m.ord), '[]'::jsonb)::text
           FROM (
               SELECT 1 AS ord, 'Trim'::text AS field, v.trim::text AS text WHERE v.trim ILIKE @Pattern ESCAPE '\'
               UNION ALL SELECT 2, 'Color', v.color::text WHERE v.color ILIKE @Pattern ESCAPE '\'
               UNION ALL SELECT 3, 'VIN', v.vin::text WHERE v.vin ILIKE @Pattern ESCAPE '\'
               UNION ALL SELECT 4, 'Engine', v.engine::text WHERE v.engine ILIKE @Pattern ESCAPE '\'
               UNION ALL SELECT 5, 'Feature', f.name::text
                         FROM vehicle_tags t JOIN features f ON f.id = t.feature_id
                         WHERE t.vehicle_id = v.id AND f.name ILIKE @Pattern ESCAPE '\'
           ) m) END AS matches_json
FROM vehicles v
LEFT JOIN lookup_items ft ON ft.id = v.fuel_type_id
LEFT JOIN lookup_items tr ON tr.id = v.transmission_id
LEFT JOIN vehicle_images mi ON mi.vehicle_id = v.id AND mi.is_main
-- The latest-dated service record; the garage mileage is the greatest of its odometer, the latest odometer reading, and the purchase mileage.
LEFT JOIN LATERAL (
    SELECT sr.odometer_reading
    FROM service_records sr
    WHERE sr.vehicle_id = v.id
    ORDER BY sr.date_serviced DESC, sr.created_at DESC, sr.id DESC
    LIMIT 1) lr ON true
WHERE v.owner_id = @OwnerId
  AND (@Status = 'all' OR (@Status = 'active' AND v.archived_at IS NULL) OR (@Status = 'archived' AND v.archived_at IS NOT NULL))
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
  AND (@DrivetrainId::uuid IS NULL OR v.drivetrain_id = @DrivetrainId)
ORDER BY v.year DESC NULLS LAST, lower(v.make), lower(v.model), v.id
LIMIT @PageSize OFFSET @Offset;
