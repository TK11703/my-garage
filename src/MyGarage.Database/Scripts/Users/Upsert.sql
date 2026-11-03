INSERT INTO user_profiles (owner_id, email, display_name, first_seen_at, last_seen_at)
VALUES (@OwnerId, @Email, @DisplayName, @Now, @Now)
ON CONFLICT (owner_id) DO UPDATE SET
    email = EXCLUDED.email,
    display_name = EXCLUDED.display_name,
    last_seen_at = EXCLUDED.last_seen_at;
