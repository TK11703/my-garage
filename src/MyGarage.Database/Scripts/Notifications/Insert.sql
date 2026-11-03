INSERT INTO notifications (id, owner_id, kind, draft_id, title, created_at)
VALUES (@Id, @OwnerId, 'draft_ready', @DraftId, @Title, @CreatedAt);
