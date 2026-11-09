-- Apply only after clients no longer depend on legacy_category.
BEGIN;
ALTER TABLE tickets DROP COLUMN legacy_category;
COMMIT;
