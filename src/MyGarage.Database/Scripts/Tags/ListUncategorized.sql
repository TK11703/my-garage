SELECT id, name
FROM features
WHERE category IS NULL
ORDER BY created_at, id
LIMIT @Limit;
