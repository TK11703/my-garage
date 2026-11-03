-- At most two rows: enough to tell a unique match from an ambiguous address.
SELECT owner_id
FROM user_profiles
WHERE lower(email) = lower(@Email)
ORDER BY owner_id
LIMIT 2;
