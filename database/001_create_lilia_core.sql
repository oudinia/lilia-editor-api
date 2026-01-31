-- Lilia Core Database Schema
-- PostgreSQL 15+

-- Enable UUID extension
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- Users (synced from Clerk)
CREATE TABLE users (
    id VARCHAR(255) PRIMARY KEY,  -- Clerk user ID
    email VARCHAR(255) NOT NULL UNIQUE,
    name VARCHAR(255),
    image TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- Roles (3 roles: owner, editor, viewer)
CREATE TABLE roles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(50) NOT NULL UNIQUE,  -- owner, editor, viewer
    description TEXT,
    permissions JSONB NOT NULL DEFAULT '[]'  -- Array of permission strings
);

-- Teams (top-level container)
CREATE TABLE teams (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    slug VARCHAR(255) UNIQUE,
    image TEXT,
    owner_id VARCHAR(255) NOT NULL REFERENCES users(id),
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- Groups (sub-groups within team, initially 1:1 with team, hidden in UI)
CREATE TABLE groups (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    team_id UUID NOT NULL REFERENCES teams(id) ON DELETE CASCADE,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    is_default BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- User-Group membership (many-to-many)
CREATE TABLE group_members (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    group_id UUID NOT NULL REFERENCES groups(id) ON DELETE CASCADE,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id),
    role_id UUID NOT NULL REFERENCES roles(id),
    created_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(group_id, user_id)
);

-- Documents
CREATE TABLE documents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_id VARCHAR(255) NOT NULL REFERENCES users(id),
    team_id UUID REFERENCES teams(id) ON DELETE SET NULL,
    title VARCHAR(255) NOT NULL DEFAULT 'Untitled',
    language VARCHAR(10) DEFAULT 'en',
    paper_size VARCHAR(50) DEFAULT 'a4',
    font_family VARCHAR(100) DEFAULT 'serif',
    font_size INTEGER DEFAULT 12,
    is_public BOOLEAN DEFAULT FALSE,
    share_link VARCHAR(100) UNIQUE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    last_opened_at TIMESTAMPTZ,
    deleted_at TIMESTAMPTZ
);

-- Blocks
CREATE TABLE blocks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    type VARCHAR(50) NOT NULL,
    content JSONB NOT NULL DEFAULT '{}',
    sort_order INTEGER NOT NULL DEFAULT 0,
    parent_id UUID REFERENCES blocks(id) ON DELETE SET NULL,
    depth INTEGER DEFAULT 0,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- Bibliography
CREATE TABLE bibliography_entries (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    cite_key VARCHAR(100) NOT NULL,
    entry_type VARCHAR(50) NOT NULL,  -- article, book, etc.
    data JSONB NOT NULL,  -- title, authors, year, etc.
    formatted_text TEXT,  -- Pre-rendered citation
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(document_id, cite_key)
);

-- Labels
CREATE TABLE labels (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name VARCHAR(100) NOT NULL,
    color VARCHAR(7),  -- Hex color
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Document Labels (many-to-many)
CREATE TABLE document_labels (
    document_id UUID REFERENCES documents(id) ON DELETE CASCADE,
    label_id UUID REFERENCES labels(id) ON DELETE CASCADE,
    PRIMARY KEY (document_id, label_id)
);

-- Document Collaborators (individual user access)
CREATE TABLE document_collaborators (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    user_id VARCHAR(255) NOT NULL REFERENCES users(id),
    role_id UUID NOT NULL REFERENCES roles(id),
    invited_by VARCHAR(255) REFERENCES users(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(document_id, user_id)
);

-- Document Group Access (share with entire group)
CREATE TABLE document_groups (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    group_id UUID NOT NULL REFERENCES groups(id) ON DELETE CASCADE,
    role_id UUID NOT NULL REFERENCES roles(id),
    created_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(document_id, group_id)
);

-- Version History
CREATE TABLE document_versions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    version_number INTEGER NOT NULL,
    name VARCHAR(255),
    snapshot JSONB NOT NULL,  -- Full document + blocks snapshot
    created_by VARCHAR(255) REFERENCES users(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Templates
CREATE TABLE templates (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id VARCHAR(255) REFERENCES users(id) ON DELETE SET NULL,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    category VARCHAR(50),
    thumbnail TEXT,
    content JSONB NOT NULL,  -- Document structure
    is_public BOOLEAN DEFAULT FALSE,
    is_system BOOLEAN DEFAULT FALSE,
    usage_count INTEGER DEFAULT 0,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- Assets
CREATE TABLE assets (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    file_name VARCHAR(255) NOT NULL,
    file_type VARCHAR(100) NOT NULL,
    file_size BIGINT NOT NULL,
    storage_key VARCHAR(500) NOT NULL,  -- R2 key
    url TEXT,  -- Public URL
    width INTEGER,
    height INTEGER,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- User Preferences
CREATE TABLE user_preferences (
    user_id VARCHAR(255) PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    theme VARCHAR(20) DEFAULT 'system',
    default_font_family VARCHAR(100),
    default_font_size INTEGER,
    default_paper_size VARCHAR(50),
    auto_save_enabled BOOLEAN DEFAULT TRUE,
    auto_save_interval INTEGER DEFAULT 2000,
    keyboard_shortcuts JSONB DEFAULT '{}',
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- Indexes
CREATE INDEX idx_teams_owner ON teams(owner_id);
CREATE INDEX idx_groups_team ON groups(team_id);
CREATE INDEX idx_group_members_group ON group_members(group_id);
CREATE INDEX idx_group_members_user ON group_members(user_id);
CREATE INDEX idx_documents_owner ON documents(owner_id);
CREATE INDEX idx_documents_team ON documents(team_id);
CREATE INDEX idx_documents_deleted ON documents(deleted_at);
CREATE INDEX idx_blocks_document ON blocks(document_id);
CREATE INDEX idx_blocks_sort ON blocks(document_id, sort_order);
CREATE INDEX idx_bib_document ON bibliography_entries(document_id);
CREATE INDEX idx_labels_user ON labels(user_id);
CREATE INDEX idx_doc_collaborators_doc ON document_collaborators(document_id);
CREATE INDEX idx_doc_collaborators_user ON document_collaborators(user_id);
CREATE INDEX idx_doc_groups_doc ON document_groups(document_id);
CREATE INDEX idx_versions_document ON document_versions(document_id);
CREATE INDEX idx_templates_user ON templates(user_id);
CREATE INDEX idx_templates_category ON templates(category);
CREATE INDEX idx_templates_public ON templates(is_public);
CREATE INDEX idx_templates_system ON templates(is_system);
CREATE INDEX idx_assets_document ON assets(document_id);

-- Seed roles
INSERT INTO roles (id, name, description, permissions) VALUES
    ('00000000-0000-0000-0000-000000000001', 'owner', 'Full control', '["read","write","delete","manage","transfer"]'),
    ('00000000-0000-0000-0000-000000000002', 'editor', 'Can edit content', '["read","write"]'),
    ('00000000-0000-0000-0000-000000000003', 'viewer', 'Read-only access', '["read"]');

-- Team members view (for convenience)
CREATE VIEW team_members AS
SELECT DISTINCT
    g.team_id,
    gm.user_id,
    gm.role_id,
    r.name as role_name
FROM group_members gm
JOIN groups g ON gm.group_id = g.id
JOIN roles r ON gm.role_id = r.id;
