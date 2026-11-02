SELECT i.id,
       i.kind,
       i.name,
       i.owner_id IS NULL AS is_default,
       h.item_id IS NOT NULL AS is_hidden,
       i.sort_order
FROM lookup_items i
LEFT JOIN lookup_item_hidden h ON h.item_id = i.id AND h.owner_id = @OwnerId
WHERE i.id = @ItemId
  AND i.kind = @Kind
  AND (i.owner_id IS NULL OR i.owner_id = @OwnerId);
