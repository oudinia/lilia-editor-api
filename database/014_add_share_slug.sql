-- Add share_slug column for human-readable share URLs
ALTER TABLE documents ADD COLUMN IF NOT EXISTS share_slug VARCHAR(200);
