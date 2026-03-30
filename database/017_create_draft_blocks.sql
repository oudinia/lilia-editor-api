-- Draft blocks: per-user scratchpad/library blocks (not attached to any document)
CREATE TABLE IF NOT EXISTS draft_blocks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name VARCHAR(255),
    type VARCHAR(50) NOT NULL,
    content JSONB NOT NULL DEFAULT '{}'::jsonb,
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    category VARCHAR(50),
    tags JSONB DEFAULT '[]'::jsonb,
    is_favorite BOOLEAN NOT NULL DEFAULT false,
    usage_count INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_draft_blocks_user_id ON draft_blocks(user_id);
CREATE INDEX IF NOT EXISTS ix_draft_blocks_user_id_type ON draft_blocks(user_id, type);
CREATE INDEX IF NOT EXISTS ix_draft_blocks_user_id_category ON draft_blocks(user_id, category);
CREATE INDEX IF NOT EXISTS ix_draft_blocks_user_id_is_favorite ON draft_blocks(user_id, is_favorite);
