DELETE FROM lookup_item_hidden
WHERE owner_id = @OwnerId
  AND item_id = @ItemId;
