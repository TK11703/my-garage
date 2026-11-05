SELECT EXISTS (
    SELECT 1
    FROM vehicles
    WHERE owner_id = @OwnerId
      AND vin = upper(@Vin)
      AND (@ExcludeId::uuid IS NULL OR id <> @ExcludeId));
