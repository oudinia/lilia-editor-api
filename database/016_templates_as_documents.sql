-- Templates as Documents: unify templates and documents into one table
-- Templates are documents with is_template = true

-- Step 1: Add template fields to documents table
ALTER TABLE documents ADD COLUMN IF NOT EXISTS is_template BOOLEAN DEFAULT false;
ALTER TABLE documents ADD COLUMN IF NOT EXISTS template_name VARCHAR(255);
ALTER TABLE documents ADD COLUMN IF NOT EXISTS template_description TEXT;
ALTER TABLE documents ADD COLUMN IF NOT EXISTS template_category VARCHAR(50);
ALTER TABLE documents ADD COLUMN IF NOT EXISTS template_thumbnail TEXT;
ALTER TABLE documents ADD COLUMN IF NOT EXISTS is_public_template BOOLEAN DEFAULT false;
ALTER TABLE documents ADD COLUMN IF NOT EXISTS template_usage_count INTEGER DEFAULT 0;

CREATE INDEX IF NOT EXISTS idx_documents_is_template ON documents (is_template) WHERE is_template = true;
CREATE INDEX IF NOT EXISTS idx_documents_template_category ON documents (template_category) WHERE is_template = true;
CREATE INDEX IF NOT EXISTS idx_documents_public_template ON documents (is_public_template) WHERE is_public_template = true;

-- Step 2: Migrate templates to documents + blocks
-- This is done in application code (seed script) since we need to parse JSONB content into individual block rows

-- Step 3: Drop templates table (after migration is verified)
-- DROP TABLE IF EXISTS templates;
-- (Uncomment after verifying migration is complete)
