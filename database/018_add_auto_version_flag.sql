-- Add is_auto_save flag to document_versions for auto-versioning
ALTER TABLE document_versions ADD COLUMN IF NOT EXISTS is_auto_save BOOLEAN NOT NULL DEFAULT FALSE;

-- Index for efficient auto-version queries (throttle check + pruning)
CREATE INDEX IF NOT EXISTS idx_document_versions_auto_save
    ON document_versions (document_id, is_auto_save, created_at DESC);
