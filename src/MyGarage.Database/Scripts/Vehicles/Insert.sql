INSERT INTO vehicles (id, owner_id, vin, make, model, trim, year, vehicle_type_id, ownership, purchased_on, lease_ends_on,
                      engine, horsepower, fuel_type_id, transmission_id, mileage, latest_odometer_reading, color, seats, doors, drivetrain_id, wheel_size, tire_size,
                      created_at, updated_at, created_by, updated_by)
VALUES (@Id, @OwnerId, @Vin, @Make, @Model, @Trim, @Year, @VehicleTypeId, @Ownership, @PurchasedOn, @LeaseEndsOn,
        @Engine, @Horsepower, @FuelTypeId, @TransmissionId, @Mileage, @LatestOdometerReading, @Color, @Seats, @Doors, @DrivetrainId, @WheelSize, @TireSize,
        @CreatedAt, @UpdatedAt, @CreatedBy, @UpdatedBy);
