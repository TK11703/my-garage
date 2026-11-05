UPDATE features
SET category = @Category
WHERE id = @Id
  AND category IS NULL;
