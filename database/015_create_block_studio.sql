-- Block Studio: extend blocks table + new tables for studio experience

-- Add studio columns to existing blocks table
ALTER TABLE blocks ADD COLUMN IF NOT EXISTS path TEXT;
ALTER TABLE blocks ADD COLUMN IF NOT EXISTS status VARCHAR(20) DEFAULT 'draft';
ALTER TABLE blocks ADD COLUMN IF NOT EXISTS metadata JSONB DEFAULT '{}';

-- Index for tree queries (path prefix matching)
CREATE INDEX IF NOT EXISTS idx_blocks_path ON blocks (document_id, path);
CREATE INDEX IF NOT EXISTS idx_blocks_status ON blocks (document_id, status);

-- Cached per-block renders (PDF, HTML, SVG)
CREATE TABLE IF NOT EXISTS block_previews (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    block_id UUID NOT NULL REFERENCES blocks(id) ON DELETE CASCADE,
    format VARCHAR(20) NOT NULL,
    data BYTEA,
    rendered_at TIMESTAMP NOT NULL DEFAULT NOW(),
    UNIQUE(block_id, format)
);

CREATE INDEX IF NOT EXISTS idx_block_previews_block ON block_previews (block_id);

-- Studio workspace state per user per document
CREATE TABLE IF NOT EXISTS studio_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id VARCHAR(100) NOT NULL,
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    focused_block_id UUID,
    layout JSONB DEFAULT '{}',
    collapsed_ids UUID[] DEFAULT '{}',
    pinned_ids UUID[] DEFAULT '{}',
    view_mode VARCHAR(20) DEFAULT 'tree',
    last_accessed TIMESTAMP NOT NULL DEFAULT NOW(),
    UNIQUE(user_id, document_id)
);

CREATE INDEX IF NOT EXISTS idx_studio_sessions_user_doc ON studio_sessions (user_id, document_id);

-- Backfill path column for existing blocks using sort_order
-- This gives a flat path like "0001", "0002" etc. Studio will update paths on tree operations
UPDATE blocks SET path = LPAD(sort_order::TEXT, 4, '0') WHERE path IS NULL;
