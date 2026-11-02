SELECT count(*)
FROM lookup_items
WHERE owner_id = @OwnerId
  AND kind = @Kind;
