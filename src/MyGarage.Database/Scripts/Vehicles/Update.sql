UPDATE vehicles
SET nickname   = @Nickname,
    make       = @Make,
    model      = @Model,
    trim       = @Trim,
    color      = @Color,
    mileage    = @Mileage,
    doors      = @Doors,
    updated_at = @UpdatedAt,
    updated_by = @UpdatedBy
WHERE id = @Id
  AND owner_id = @OwnerId;
