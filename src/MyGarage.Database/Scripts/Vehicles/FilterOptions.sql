SELECT DISTINCT ON (lower(make)) make
FROM vehicles
WHERE owner_id = @OwnerId
ORDER BY lower(make), make;

SELECT DISTINCT ON (lower(color)) color
FROM vehicles
WHERE owner_id = @OwnerId
  AND color IS NOT NULL
ORDER BY lower(color), color;

SELECT DISTINCT ON (t.name_key) t.name
FROM vehicle_tags t
JOIN vehicles v ON v.id = t.vehicle_id
WHERE v.owner_id = @OwnerId
ORDER BY t.name_key, t.name;
