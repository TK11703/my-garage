-- How many entries Vehicles/TimelinePage.sql would return for the same vehicle and kind.
SELECT
    (SELECT count(*) FROM service_records WHERE vehicle_id = @VehicleId AND (@Kind::text IS NULL OR @Kind = 'service'))
  + (SELECT count(*) FROM fuel_energy_entries WHERE vehicle_id = @VehicleId AND (@Kind::text IS NULL OR @Kind = 'fuel-energy'))
  + (SELECT count(*) FROM inspections WHERE vehicle_id = @VehicleId AND (@Kind::text IS NULL OR @Kind = 'inspection'))
  + (SELECT count(*) FROM registrations WHERE vehicle_id = @VehicleId AND (@Kind::text IS NULL OR @Kind = 'registration'))
  + (SELECT count(*) FROM insurance_policies WHERE vehicle_id = @VehicleId AND (@Kind::text IS NULL OR @Kind = 'insurance'))
  + (SELECT count(*) FROM taxes WHERE vehicle_id = @VehicleId AND (@Kind::text IS NULL OR @Kind = 'tax'))
  + (SELECT count(*) FROM journal_notes WHERE vehicle_id = @VehicleId AND (@Kind::text IS NULL OR @Kind = 'journal')) AS total
FROM vehicles v
WHERE v.id = @VehicleId AND v.owner_id = @OwnerId;
