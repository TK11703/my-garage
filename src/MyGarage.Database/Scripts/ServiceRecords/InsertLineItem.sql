INSERT INTO service_line_items (id, service_record_id, description, flat_cost, labor_hours, labor_rate, labor_cost, total_cost, created_at, updated_at, created_by, updated_by)
VALUES (@LineItemId, @RecordId, @Description, @FlatCost, @LaborHours, @LaborRate, @LaborCost, @TotalCost, @Now, @Now, @OwnerId, @OwnerId);
