SELECT f.name
FROM features f
WHERE f.name ILIKE @Pattern ESCAPE '\'
  AND EXISTS (SELECT 1 FROM vehicle_tags t WHERE t.feature_id = f.id)
ORDER BY (SELECT count(*) FROM vehicle_tags t WHERE t.feature_id = f.id) DESC, f.name_key
LIMIT @Limit;
