-- Audit log table for tracking business-critical events
CREATE TABLE IF NOT EXISTS audit_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id TEXT NOT NULL,
    action TEXT NOT NULL,           -- e.g. 'document.create', 'document.delete', 'export.pdf'
    entity_type TEXT NOT NULL,      -- e.g. 'Document', 'Team', 'Template'
    entity_id TEXT,
    details JSONB,                  -- Additional context (title, format, etc.)
    ip_address TEXT,
    user_agent TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_audit_logs_user_id ON audit_logs(user_id);
CREATE INDEX idx_audit_logs_action ON audit_logs(action);
CREATE INDEX idx_audit_logs_entity ON audit_logs(entity_type, entity_id);
CREATE INDEX idx_audit_logs_created_at ON audit_logs(created_at);
