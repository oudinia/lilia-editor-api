-- Add full-text search for help articles
-- Denormalized search_text column for fast GIN-indexed search

ALTER TABLE documents ADD COLUMN IF NOT EXISTS search_text TEXT;

-- Populate search_text from title + all block text content
UPDATE documents d SET search_text = (
  SELECT d.title || ' ' || COALESCE(string_agg(b.content->>'text', ' '), '')
  FROM blocks b WHERE b.document_id = d.id
)
WHERE d.is_help_content = true;

-- GIN index for full-text search
CREATE INDEX IF NOT EXISTS idx_help_search_gin
  ON documents USING gin(to_tsvector('english', COALESCE(search_text, '')))
  WHERE is_help_content = true;
