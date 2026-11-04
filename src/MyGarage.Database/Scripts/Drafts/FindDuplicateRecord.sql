-- A record of the same kind on the owner's vehicle with the same date and total (tax records carry no date, so they never match).
SELECT EXISTS (
    SELECT 1
    FROM service_records sr
    JOIN vehicles v ON v.id = sr.vehicle_id
    WHERE @Kind = 'service_record'
      AND v.owner_id = @OwnerId
      AND sr.vehicle_id = @VehicleId
      AND sr.date_serviced = @Date
      AND (SELECT COALESCE(sum(li.total_cost), 0) FROM service_line_items li WHERE li.service_record_id = sr.id) + COALESCE(sr.tax_amount, 0) = @Total)
OR EXISTS (
    SELECT 1
    FROM fuel_energy_entries fe
    JOIN vehicles v ON v.id = fe.vehicle_id
    WHERE @Kind IN ('fuel', 'energy')
      AND fe.kind = @Kind
      AND v.owner_id = @OwnerId
      AND fe.vehicle_id = @VehicleId
      AND fe.entry_date = @Date
      AND fe.total_cost = @Total)
OR EXISTS (
    SELECT 1
    FROM inspections i
    JOIN vehicles v ON v.id = i.vehicle_id
    WHERE @Kind = 'inspection'
      AND v.owner_id = @OwnerId
      AND i.vehicle_id = @VehicleId
      AND i.inspection_date = @Date
      AND i.cost = @Total)
OR EXISTS (
    SELECT 1
    FROM registrations r
    JOIN vehicles v ON v.id = r.vehicle_id
    WHERE @Kind = 'registration'
      AND v.owner_id = @OwnerId
      AND r.vehicle_id = @VehicleId
      AND r.expiration_date = @Date
      AND r.cost = @Total)
OR EXISTS (
    SELECT 1
    FROM insurance_policies p
    JOIN vehicles v ON v.id = p.vehicle_id
    WHERE @Kind = 'insurance'
      AND v.owner_id = @OwnerId
      AND p.vehicle_id = @VehicleId
      AND p.start_date = @Date
      AND p.cost = @Total);
