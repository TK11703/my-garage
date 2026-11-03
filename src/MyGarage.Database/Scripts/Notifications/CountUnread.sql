SELECT count(*)::int FROM notifications WHERE owner_id = @OwnerId AND read_at IS NULL;
