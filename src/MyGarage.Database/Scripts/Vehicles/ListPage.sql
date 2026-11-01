SELECT v.id, v.nickname, v.make, v.model, v.mileage, mi.id AS main_image_id
FROM vehicles v
LEFT JOIN vehicle_images mi ON mi.vehicle_id = v.id AND mi.is_main
WHERE v.owner_id = @OwnerId
  AND (@Pattern::text IS NULL
       OR v.nickname ILIKE @Pattern ESCAPE '\'
       OR v.make ILIKE @Pattern ESCAPE '\'
       OR v.model ILIKE @Pattern ESCAPE '\'
       OR v.trim ILIKE @Pattern ESCAPE '\'
       OR v.color ILIKE @Pattern ESCAPE '\'
       OR EXISTS (SELECT 1 FROM vehicle_tags t WHERE t.vehicle_id = v.id AND t.name ILIKE @Pattern ESCAPE '\'))
  AND (@Make::text IS NULL OR lower(v.make) = lower(@Make))
  AND (@Color::text IS NULL OR lower(v.color) = lower(@Color))
  AND (@Doors::int IS NULL OR v.doors = @Doors)
  AND (@Tag::text IS NULL OR EXISTS (SELECT 1 FROM vehicle_tags t WHERE t.vehicle_id = v.id AND t.name_key = lower(@Tag)))
  AND (@MinMileage::int IS NULL OR v.mileage >= @MinMileage)
  AND (@MaxMileage::int IS NULL OR v.mileage <= @MaxMileage)
ORDER BY lower(v.nickname), v.id
LIMIT @PageSize OFFSET @Offset;
