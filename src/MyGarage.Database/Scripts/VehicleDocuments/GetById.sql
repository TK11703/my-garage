SELECT d.id, d.vehicle_id, d.file_name, d.content_type, d.size_bytes, d.source_kind, d.source_record_id, d.created_at, d.created_by
FROM vehicle_documents d
JOIN vehicles v ON v.id = d.vehicle_id
WHERE d.id = @DocumentId
  AND d.vehicle_id = @VehicleId
  AND v.owner_id = @OwnerId;
