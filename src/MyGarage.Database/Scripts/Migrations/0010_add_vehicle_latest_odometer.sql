-- The garage mileage is the greatest of this, the latest service record's odometer, and the mileage on purchase or lease.
ALTER TABLE vehicles
    ADD COLUMN latest_odometer_reading integer NULL CHECK (latest_odometer_reading BETWEEN 0 AND 9999999);
