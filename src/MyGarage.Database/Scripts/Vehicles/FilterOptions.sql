-- Every statement takes @Status (active, archived, or all) so the choices match the vehicles the list is showing.
SELECT DISTINCT ON (lower(v.make)) v.make
FROM vehicles v
WHERE v.owner_id = @OwnerId
  AND (@Status = 'all' OR (@Status = 'active' AND v.archived_at IS NULL) OR (@Status = 'archived' AND v.archived_at IS NOT NULL))
ORDER BY lower(v.make), v.make;

SELECT DISTINCT ON (lower(v.color)) v.color
FROM vehicles v
WHERE v.owner_id = @OwnerId
  AND v.color IS NOT NULL
  AND (@Status = 'all' OR (@Status = 'active' AND v.archived_at IS NULL) OR (@Status = 'archived' AND v.archived_at IS NOT NULL))
ORDER BY lower(v.color), v.color;

SELECT DISTINCT ON (f.name_key) f.name
FROM vehicle_tags t
JOIN features f ON f.id = t.feature_id
JOIN vehicles v ON v.id = t.vehicle_id
WHERE v.owner_id = @OwnerId
  AND (@Status = 'all' OR (@Status = 'active' AND v.archived_at IS NULL) OR (@Status = 'archived' AND v.archived_at IS NOT NULL))
ORDER BY f.name_key, f.name;

SELECT DISTINCT i.id, i.name, i.name_key
FROM lookup_items i
JOIN vehicles v ON v.vehicle_type_id = i.id
WHERE v.owner_id = @OwnerId
  AND (@Status = 'all' OR (@Status = 'active' AND v.archived_at IS NULL) OR (@Status = 'archived' AND v.archived_at IS NOT NULL))
ORDER BY i.name_key, i.id;

SELECT DISTINCT i.id, i.name, i.name_key
FROM lookup_items i
JOIN vehicles v ON v.fuel_type_id = i.id
WHERE v.owner_id = @OwnerId
  AND (@Status = 'all' OR (@Status = 'active' AND v.archived_at IS NULL) OR (@Status = 'archived' AND v.archived_at IS NOT NULL))
ORDER BY i.name_key, i.id;

SELECT DISTINCT i.id, i.name, i.name_key
FROM lookup_items i
JOIN vehicles v ON v.transmission_id = i.id
WHERE v.owner_id = @OwnerId
  AND (@Status = 'all' OR (@Status = 'active' AND v.archived_at IS NULL) OR (@Status = 'archived' AND v.archived_at IS NOT NULL))
ORDER BY i.name_key, i.id;

SELECT DISTINCT i.id, i.name, i.name_key
FROM lookup_items i
JOIN vehicles v ON v.drivetrain_id = i.id
WHERE v.owner_id = @OwnerId
  AND (@Status = 'all' OR (@Status = 'active' AND v.archived_at IS NULL) OR (@Status = 'archived' AND v.archived_at IS NOT NULL))
ORDER BY i.name_key, i.id;
