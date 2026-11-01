DELETE FROM journal_notes n
USING vehicles v
WHERE n.id = @NoteId
  AND n.vehicle_id = @VehicleId
  AND v.id = n.vehicle_id
  AND v.owner_id = @OwnerId;
