-- Keeps the draft's own stored attachment as a vehicle document; nothing is inserted when the draft has no attachment or the vehicle is not the owner's.
INSERT INTO vehicle_documents (id, vehicle_id, file_name, content_type, size_bytes, source_kind, source_record_id, created_at, created_by)
SELECT @DocumentId, v.id, a.file_name, a.content_type, a.size_bytes, @SourceKind, @SourceRecordId, @Now, @OwnerId
FROM maintenance_drafts d
JOIN email_attachments a ON a.id = d.attachment_id AND a.owner_id = d.owner_id
JOIN vehicles v ON v.id = @VehicleId AND v.owner_id = d.owner_id
WHERE d.id = @DraftId
  AND d.owner_id = @OwnerId
RETURNING id;
