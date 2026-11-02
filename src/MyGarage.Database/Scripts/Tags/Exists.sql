SELECT EXISTS (
    SELECT 1
    FROM vehicle_tags t
    JOIN features f ON f.id = t.feature_id
    WHERE t.vehicle_id = @VehicleId
      AND f.name_key = lower(@Name));
