SELECT count(*)
FROM journal_notes n
JOIN vehicles v ON v.id = n.vehicle_id
WHERE n.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
