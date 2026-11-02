INSERT INTO lookup_items (id, kind, owner_id, name, sort_order, created_at, created_by)
VALUES (@Id, @Kind, @OwnerId, @Name, 0, @CreatedAt, @OwnerId);
