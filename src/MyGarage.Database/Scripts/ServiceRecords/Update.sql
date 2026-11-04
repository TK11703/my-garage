UPDATE service_records sr
SET date_serviced    = @DateServiced,
    odometer_reading = @OdometerReading,
    shop_or_person   = @ShopOrPerson,
    tax_amount       = @TaxAmount,
    updated_at       = @Now,
    updated_by       = @OwnerId
FROM vehicles v
WHERE sr.id = @RecordId
  AND sr.vehicle_id = @VehicleId
  AND v.id = sr.vehicle_id
  AND v.owner_id = @OwnerId
RETURNING sr.id;
