SELECT DISTINCT ON (lower(make)) make
FROM vehicles
WHERE owner_id = @OwnerId
ORDER BY lower(make), make;

SELECT DISTINCT ON (lower(color)) color
FROM vehicles
WHERE owner_id = @OwnerId
  AND color IS NOT NULL
ORDER BY lower(color), color;

SELECT DISTINCT ON (f.name_key) f.name
FROM vehicle_tags t
JOIN features f ON f.id = t.feature_id
JOIN vehicles v ON v.id = t.vehicle_id
WHERE v.owner_id = @OwnerId
ORDER BY f.name_key, f.name;

SELECT DISTINCT i.id, i.name, i.name_key
FROM lookup_items i
JOIN vehicles v ON v.vehicle_type_id = i.id
WHERE v.owner_id = @OwnerId
ORDER BY i.name_key, i.id;

SELECT DISTINCT i.id, i.name, i.name_key
FROM lookup_items i
JOIN vehicles v ON v.fuel_type_id = i.id
WHERE v.owner_id = @OwnerId
ORDER BY i.name_key, i.id;

SELECT DISTINCT i.id, i.name, i.name_key
FROM lookup_items i
JOIN vehicles v ON v.transmission_id = i.id
WHERE v.owner_id = @OwnerId
ORDER BY i.name_key, i.id;

SELECT DISTINCT i.id, i.name, i.name_key
FROM lookup_items i
JOIN vehicles v ON v.drivetrain_id = i.id
WHERE v.owner_id = @OwnerId
ORDER BY i.name_key, i.id;
