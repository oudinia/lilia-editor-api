-- Add raw_import_data column to store the raw output from import engines (e.g. Mathpix markdown)
-- This enables integration testing without re-calling external APIs.
ALTER TABLE import_review_sessions ADD COLUMN IF NOT EXISTS raw_import_data TEXT;
