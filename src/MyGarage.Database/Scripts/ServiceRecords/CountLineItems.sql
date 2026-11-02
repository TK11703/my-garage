SELECT count(*)
FROM service_line_items
WHERE service_record_id = @RecordId;
