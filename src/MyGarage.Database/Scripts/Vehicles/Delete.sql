DELETE FROM vehicles
WHERE id = @Id
  AND owner_id = @OwnerId;
