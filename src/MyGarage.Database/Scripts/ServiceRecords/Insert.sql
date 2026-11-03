INSERT INTO service_records (id, vehicle_id, date_serviced, odometer_reading, shop_or_person, created_at, updated_at, created_by, updated_by, from_email)
SELECT @Id, v.id, @DateServiced, @OdometerReading, @ShopOrPerson, @Now, @Now, @OwnerId, @OwnerId, @FromEmail
FROM vehicles v
WHERE v.id = @VehicleId
  AND v.owner_id = @OwnerId
RETURNING id;
