INSERT INTO lookup_item_hidden (owner_id, item_id, hidden_at)
VALUES (@OwnerId, @ItemId, @HiddenAt)
ON CONFLICT DO NOTHING;
