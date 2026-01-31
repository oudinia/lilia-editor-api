-- Lilia Jobs Database Schema (for lilia-docx-api)
-- PostgreSQL 15+

-- Enable UUID extension
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- Tenants (for lilia-docx-api)
CREATE TABLE tenants (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    api_key_hash VARCHAR(255) NOT NULL UNIQUE,
    rate_limit INTEGER DEFAULT 100,  -- Requests per minute
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Jobs
CREATE TABLE jobs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id VARCHAR(255) NOT NULL,  -- API key tenant
    user_id VARCHAR(255),  -- Optional Clerk user
    document_id UUID,
    job_type VARCHAR(50) NOT NULL,  -- IMPORT, EXPORT, CONVERT
    direction VARCHAR(20),  -- IN, OUT
    source_format VARCHAR(20),
    target_format VARCHAR(20),
    status VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    progress INTEGER DEFAULT 0,
    input_file_name VARCHAR(255),
    input_file_key VARCHAR(500),  -- R2/Storage key
    output_file_name VARCHAR(255),
    output_file_key VARCHAR(500),  -- R2/Storage key
    error_message TEXT,
    retry_count INTEGER DEFAULT 0,
    max_retries INTEGER DEFAULT 3,
    metadata JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    started_at TIMESTAMPTZ,
    completed_at TIMESTAMPTZ,
    expires_at TIMESTAMPTZ  -- When to delete files
);

-- Indexes
CREATE INDEX idx_jobs_tenant ON jobs(tenant_id);
CREATE INDEX idx_jobs_user ON jobs(user_id);
CREATE INDEX idx_jobs_status ON jobs(status);
CREATE INDEX idx_jobs_created ON jobs(created_at);
CREATE INDEX idx_jobs_type ON jobs(job_type);
CREATE INDEX idx_jobs_expires ON jobs(expires_at);

-- Job status types: PENDING, PROCESSING, COMPLETED, FAILED, CANCELLED
