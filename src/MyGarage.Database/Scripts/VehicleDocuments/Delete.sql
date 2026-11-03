DELETE FROM vehicle_documents d
USING vehicles v
WHERE d.id = @DocumentId
  AND d.vehicle_id = @VehicleId
  AND v.id = d.vehicle_id
  AND v.owner_id = @OwnerId
RETURNING d.id;
