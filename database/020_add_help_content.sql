-- Add help content fields to documents table
-- Help articles are stored as regular documents with is_help_content = true

ALTER TABLE documents
  ADD COLUMN IF NOT EXISTS is_help_content BOOLEAN NOT NULL DEFAULT false,
  ADD COLUMN IF NOT EXISTS help_category VARCHAR(50),
  ADD COLUMN IF NOT EXISTS help_order INT NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS help_slug VARCHAR(200);

-- Index for querying help content efficiently
CREATE INDEX IF NOT EXISTS idx_documents_help
  ON documents (is_help_content, help_category, help_order)
  WHERE is_help_content = true;

-- Unique slug for URL-friendly lookups
CREATE UNIQUE INDEX IF NOT EXISTS idx_documents_help_slug
  ON documents (help_slug)
  WHERE help_slug IS NOT NULL;
