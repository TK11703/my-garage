-- NULL means the feature has not been categorized yet; it is shown under Other until the categorizer fills it in.
ALTER TABLE features ADD COLUMN category varchar(40) NULL;

CREATE INDEX ix_features_uncategorized ON features (created_at) WHERE category IS NULL;
