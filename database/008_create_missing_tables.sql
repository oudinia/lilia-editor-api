-- 008_create_missing_tables.sql
-- Creates tables expected by EF Core but missing from earlier scripts

-- 1. Authentication tables (Better Auth)
CREATE TABLE IF NOT EXISTS accounts (
    id VARCHAR(255) PRIMARY KEY,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    account_id VARCHAR(255) NOT NULL,
    provider_id VARCHAR(255) NOT NULL,
    access_token TEXT,
    refresh_token TEXT,
    id_token TEXT,
    access_token_expires_at TIMESTAMPTZ,
    refresh_token_expires_at TIMESTAMPTZ,
    scope VARCHAR(500),
    password TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_accounts_user ON accounts(user_id);

CREATE TABLE IF NOT EXISTS sessions (
    id VARCHAR(255) PRIMARY KEY,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token TEXT NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    ip_address VARCHAR(255),
    user_agent TEXT,
    impersonated_by VARCHAR(255),
    active_organization_id VARCHAR(255),
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_sessions_token ON sessions(token);
CREATE INDEX IF NOT EXISTS idx_sessions_user ON sessions(user_id);

CREATE TABLE IF NOT EXISTS passkeys (
    id VARCHAR(255) PRIMARY KEY,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name VARCHAR(255),
    public_key TEXT NOT NULL,
    credential_id TEXT NOT NULL,
    counter INTEGER NOT NULL,
    device_type VARCHAR(255) NOT NULL,
    backed_up BOOLEAN,
    transports TEXT,
    created_at TIMESTAMPTZ,
    aaguid VARCHAR(255)
);
CREATE INDEX IF NOT EXISTS idx_passkeys_user ON passkeys(user_id);
CREATE INDEX IF NOT EXISTS idx_passkeys_credential ON passkeys(credential_id);

CREATE TABLE IF NOT EXISTS verifications (
    id VARCHAR(255) PRIMARY KEY,
    identifier TEXT NOT NULL,
    value TEXT NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_verifications_identifier ON verifications(identifier);

CREATE TABLE IF NOT EXISTS two_factors (
    id VARCHAR(255) PRIMARY KEY,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    secret TEXT NOT NULL,
    backup_codes TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_two_factors_secret ON two_factors(secret);
CREATE INDEX IF NOT EXISTS idx_two_factors_user ON two_factors(user_id);

-- 2. Organization tables
CREATE TABLE IF NOT EXISTS organizations (
    id VARCHAR(255) PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    slug VARCHAR(255) NOT NULL UNIQUE,
    logo TEXT,
    metadata TEXT,
    payments_customer_id VARCHAR(255),
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_organizations_slug ON organizations(slug);

CREATE TABLE IF NOT EXISTS organization_members (
    id VARCHAR(255) PRIMARY KEY,
    organization_id VARCHAR(255) NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role VARCHAR(50) NOT NULL DEFAULT 'member',
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_organization_members_org ON organization_members(organization_id);
CREATE INDEX IF NOT EXISTS idx_organization_members_user ON organization_members(user_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_organization_members_unique ON organization_members(organization_id, user_id);

CREATE TABLE IF NOT EXISTS invitations (
    id VARCHAR(255) PRIMARY KEY,
    organization_id VARCHAR(255) NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    email VARCHAR(255) NOT NULL,
    role VARCHAR(50),
    status VARCHAR(20) NOT NULL DEFAULT 'pending',
    expires_at TIMESTAMPTZ NOT NULL,
    inviter_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_invitations_org ON invitations(organization_id);
CREATE INDEX IF NOT EXISTS idx_invitations_email ON invitations(email);

CREATE TABLE IF NOT EXISTS purchases (
    id VARCHAR(255) PRIMARY KEY,
    organization_id VARCHAR(255) REFERENCES organizations(id) ON DELETE CASCADE,
    user_id VARCHAR(255) REFERENCES users(id) ON DELETE CASCADE,
    type VARCHAR(20) NOT NULL,
    customer_id VARCHAR(255) NOT NULL,
    subscription_id VARCHAR(255),
    product_id VARCHAR(255) NOT NULL,
    status VARCHAR(50),
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_purchases_subscription ON purchases(subscription_id);

-- 3. AI table
CREATE TABLE IF NOT EXISTS ai_chats (
    id VARCHAR(255) PRIMARY KEY,
    organization_id VARCHAR(255) REFERENCES organizations(id) ON DELETE CASCADE,
    user_id VARCHAR(255) REFERENCES users(id) ON DELETE CASCADE,
    title VARCHAR(500),
    messages JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);

-- 4. Document feature tables
CREATE TABLE IF NOT EXISTS comments (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    block_id UUID,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    content TEXT NOT NULL,
    resolved BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_comments_document ON comments(document_id);
CREATE INDEX IF NOT EXISTS idx_comments_block ON comments(block_id);
CREATE INDEX IF NOT EXISTS idx_comments_user ON comments(user_id);

CREATE TABLE IF NOT EXISTS comment_replies (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    comment_id UUID NOT NULL REFERENCES comments(id) ON DELETE CASCADE,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    content TEXT NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_comment_replies_comment ON comment_replies(comment_id);

CREATE TABLE IF NOT EXISTS document_snapshots (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    created_by VARCHAR(255) REFERENCES users(id) ON DELETE SET NULL,
    name VARCHAR(255),
    blocks_snapshot JSONB NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_document_snapshots_doc ON document_snapshots(document_id);

CREATE TABLE IF NOT EXISTS conversion_audits (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id UUID REFERENCES jobs(id) ON DELETE CASCADE,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    action VARCHAR(100) NOT NULL,
    details JSONB,
    timestamp TIMESTAMPTZ DEFAULT NOW(),
    duration_ms INTEGER
);
CREATE INDEX IF NOT EXISTS idx_conversion_audits_job ON conversion_audits(job_id);
CREATE INDEX IF NOT EXISTS idx_conversion_audits_user ON conversion_audits(user_id);
CREATE INDEX IF NOT EXISTS idx_conversion_audits_user_timestamp ON conversion_audits(user_id, timestamp);

CREATE TABLE IF NOT EXISTS sync_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    action VARCHAR(50) NOT NULL,
    sync_version INTEGER NOT NULL,
    metadata JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_sync_history_document ON sync_history(document_id);
CREATE INDEX IF NOT EXISTS idx_sync_history_user ON sync_history(user_id);

-- 5. Library tables (formulas + snippets)
CREATE TABLE IF NOT EXISTS formulas (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id VARCHAR(255) REFERENCES users(id) ON DELETE CASCADE,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    latex_content TEXT NOT NULL,
    lml_content TEXT,
    category VARCHAR(50) NOT NULL,
    subcategory VARCHAR(50),
    tags JSONB NOT NULL DEFAULT '[]',
    is_favorite BOOLEAN NOT NULL DEFAULT FALSE,
    is_system BOOLEAN NOT NULL DEFAULT FALSE,
    usage_count INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_formulas_user ON formulas(user_id);
CREATE INDEX IF NOT EXISTS idx_formulas_category ON formulas(category);
CREATE INDEX IF NOT EXISTS idx_formulas_system ON formulas(is_system);
CREATE INDEX IF NOT EXISTS idx_formulas_user_category ON formulas(user_id, category);

CREATE TABLE IF NOT EXISTS snippets (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id VARCHAR(255) REFERENCES users(id) ON DELETE CASCADE,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    latex_content TEXT NOT NULL,
    block_type VARCHAR(50) NOT NULL,
    category VARCHAR(50) NOT NULL,
    required_packages JSONB NOT NULL DEFAULT '[]',
    preamble TEXT,
    tags JSONB NOT NULL DEFAULT '[]',
    is_favorite BOOLEAN NOT NULL DEFAULT FALSE,
    is_system BOOLEAN NOT NULL DEFAULT FALSE,
    usage_count INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_snippets_user ON snippets(user_id);
CREATE INDEX IF NOT EXISTS idx_snippets_category ON snippets(category);
CREATE INDEX IF NOT EXISTS idx_snippets_system ON snippets(is_system);
CREATE INDEX IF NOT EXISTS idx_snippets_user_category ON snippets(user_id, category);

-- 6. Import review tables
CREATE TABLE IF NOT EXISTS import_review_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id UUID REFERENCES jobs(id) ON DELETE SET NULL,
    owner_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    document_title VARCHAR(500) NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'in_progress',
    original_warnings JSONB,
    document_id UUID REFERENCES documents(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    expires_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_import_review_sessions_owner ON import_review_sessions(owner_id);
CREATE INDEX IF NOT EXISTS idx_import_review_sessions_job ON import_review_sessions(job_id);

CREATE TABLE IF NOT EXISTS import_block_reviews (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES import_review_sessions(id) ON DELETE CASCADE,
    block_index INTEGER NOT NULL,
    block_id VARCHAR(255) NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'pending',
    reviewed_by VARCHAR(255) REFERENCES users(id) ON DELETE SET NULL,
    reviewed_at TIMESTAMPTZ,
    original_content JSONB NOT NULL,
    original_type VARCHAR(50) NOT NULL,
    current_content JSONB,
    current_type VARCHAR(50),
    confidence INTEGER,
    warnings JSONB,
    sort_order INTEGER NOT NULL,
    depth INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_import_block_reviews_session ON import_block_reviews(session_id);
CREATE INDEX IF NOT EXISTS idx_import_block_reviews_session_block ON import_block_reviews(session_id, block_id);
CREATE INDEX IF NOT EXISTS idx_import_block_reviews_session_status ON import_block_reviews(session_id, status);

CREATE TABLE IF NOT EXISTS import_review_collaborators (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES import_review_sessions(id) ON DELETE CASCADE,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role VARCHAR(20) NOT NULL DEFAULT 'reviewer',
    invited_by VARCHAR(255) REFERENCES users(id) ON DELETE SET NULL,
    invited_at TIMESTAMPTZ DEFAULT NOW(),
    last_active_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_import_review_collaborators_session ON import_review_collaborators(session_id);
CREATE INDEX IF NOT EXISTS idx_import_review_collaborators_user ON import_review_collaborators(user_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_import_review_collaborators_unique ON import_review_collaborators(session_id, user_id);

CREATE TABLE IF NOT EXISTS import_block_comments (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES import_review_sessions(id) ON DELETE CASCADE,
    block_id VARCHAR(255) NOT NULL,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    content TEXT NOT NULL,
    resolved BOOLEAN NOT NULL DEFAULT FALSE,
    resolved_by VARCHAR(255) REFERENCES users(id) ON DELETE SET NULL,
    resolved_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_import_block_comments_session ON import_block_comments(session_id);
CREATE INDEX IF NOT EXISTS idx_import_block_comments_user ON import_block_comments(user_id);
CREATE INDEX IF NOT EXISTS idx_import_block_comments_session_block ON import_block_comments(session_id, block_id);

CREATE TABLE IF NOT EXISTS import_review_activities (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES import_review_sessions(id) ON DELETE CASCADE,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    action VARCHAR(100) NOT NULL,
    block_id VARCHAR(255),
    details JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_import_review_activities_session ON import_review_activities(session_id);
CREATE INDEX IF NOT EXISTS idx_import_review_activities_user ON import_review_activities(user_id);
CREATE INDEX IF NOT EXISTS idx_import_review_activities_session_created ON import_review_activities(session_id, created_at);
