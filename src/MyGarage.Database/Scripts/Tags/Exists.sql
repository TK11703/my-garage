SELECT EXISTS (
    SELECT 1
    FROM vehicle_tags
    WHERE vehicle_id = @VehicleId
      AND name_key = lower(@Name));
