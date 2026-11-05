INSERT INTO features (id, name, created_at, created_by)
VALUES (@Id, @Name, @CreatedAt, @OwnerId)
ON CONFLICT (name_key) DO UPDATE SET name = features.name
RETURNING id, name, category;
