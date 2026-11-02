INSERT INTO service_line_items (id, service_record_id, description, flat_cost, labor_hours, labor_rate, total_cost, created_at, updated_at, created_by, updated_by)
VALUES (@LineItemId, @RecordId, @Description, @FlatCost, @LaborHours, @LaborRate, @TotalCost, @Now, @Now, @OwnerId, @OwnerId);
