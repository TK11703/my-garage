-- Only ever raises the reading: the mileage must be above the greatest of the latest service record's odometer, the latest reading, and the purchase mileage.
UPDATE vehicles v
SET latest_odometer_reading = @Mileage,
    updated_at = @Now,
    updated_by = @OwnerId
WHERE v.id = @VehicleId
  AND v.owner_id = @OwnerId
  AND @Mileage > COALESCE(
      GREATEST(
          (SELECT sr.odometer_reading
           FROM service_records sr
           WHERE sr.vehicle_id = v.id
           ORDER BY sr.date_serviced DESC, sr.created_at DESC, sr.id DESC
           LIMIT 1),
          v.latest_odometer_reading,
          v.mileage),
      -1);
