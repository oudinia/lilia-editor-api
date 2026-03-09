-- Document pending invites for unregistered users
CREATE TABLE IF NOT EXISTS document_pending_invites (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    email VARCHAR(320) NOT NULL,
    role VARCHAR(50) NOT NULL,
    invited_by VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    status VARCHAR(20) NOT NULL DEFAULT 'pending',
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMP NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_pending_invites_email ON document_pending_invites(email);
CREATE INDEX IF NOT EXISTS idx_pending_invites_status ON document_pending_invites(status);
CREATE UNIQUE INDEX IF NOT EXISTS uq_pending_invite_doc_email ON document_pending_invites(document_id, email) WHERE (status = 'pending');
