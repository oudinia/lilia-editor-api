CREATE TABLE IF NOT EXISTS feedback (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id VARCHAR(255) REFERENCES users(id) ON DELETE SET NULL,
    type VARCHAR(50) NOT NULL DEFAULT 'general',
    message TEXT NOT NULL,
    page VARCHAR(500),
    block_type VARCHAR(100),
    block_id VARCHAR(255),
    document_id VARCHAR(255),
    status VARCHAR(50) NOT NULL DEFAULT 'new',
    metadata JSONB,
    response TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX idx_feedback_status ON feedback (status, created_at DESC);
CREATE INDEX idx_feedback_user ON feedback (user_id);
CREATE INDEX idx_feedback_type ON feedback (type);
