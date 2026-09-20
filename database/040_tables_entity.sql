-- Tables as their own thing.
--
-- Until now a table was a block inside a document and nothing else. This makes
-- it an entity an author owns: their own set of tables, usable on their own, and
-- optionally attached to any number of documents.
--
-- The link is a REFERENCE, not a copy. A table in three papers is one row; edit
-- it once and all three change. That is the point of the many-to-many, and it is
-- the question to keep in mind when adding anything here — a feature that
-- silently forks a table breaks the promise.

CREATE TABLE IF NOT EXISTS tables (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_id    VARCHAR(255) NOT NULL REFERENCES users(id) ON DELETE CASCADE,

    -- The caption is the table's name. Empty is normal and expected: the
    -- listing shows a preview rather than a name for exactly this reason.
    caption     TEXT NOT NULL DEFAULT '',
    -- \label{tab:…}, stored with its prefix as the editor stores it.
    label       TEXT NOT NULL DEFAULT '',

    -- The same shape a `table` block's content has, so the editor's
    -- blockToTableData reads it unchanged: headers, rows, columnAlign,
    -- hasHeader, borders. Cells may be strings or {content, colspan, rowspan}.
    content     JSONB NOT NULL DEFAULT '{}'::jsonb,

    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    -- Tables get their own timestamp, which blocks never had. "Recently edited"
    -- is answerable here without approximating it from a document's date.
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_at  TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_tables_owner       ON tables(owner_id) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_tables_owner_recent ON tables(owner_id, updated_at DESC) WHERE deleted_at IS NULL;

-- Which documents use which tables. Optional on both sides: a table need never
-- be in a document, and a document need never have one.
CREATE TABLE IF NOT EXISTS document_tables (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    table_id    UUID NOT NULL REFERENCES tables(id)    ON DELETE CASCADE,

    -- The block that renders this table in that document, when there is one, so
    -- the reference and the thing on the page stay connected.
    block_id    UUID REFERENCES blocks(id) ON DELETE SET NULL,

    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(document_id, table_id)
);

CREATE INDEX IF NOT EXISTS idx_document_tables_document ON document_tables(document_id);
CREATE INDEX IF NOT EXISTS idx_document_tables_table    ON document_tables(table_id);

-- Sharing, mirroring document_collaborators exactly — same roles table, same
-- shape — so permission checks can be written once against either.
CREATE TABLE IF NOT EXISTS table_collaborators (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    table_id    UUID NOT NULL REFERENCES tables(id) ON DELETE CASCADE,
    user_id     VARCHAR(255) NOT NULL REFERENCES users(id),
    role_id     UUID NOT NULL REFERENCES roles(id),
    invited_by  VARCHAR(255) REFERENCES users(id) ON DELETE SET NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(table_id, user_id)
);

CREATE INDEX IF NOT EXISTS idx_table_collaborators_user ON table_collaborators(user_id);

COMMENT ON TABLE  tables            IS 'An author''s table, owned independently of any document.';
COMMENT ON TABLE  document_tables   IS 'Which documents reference which tables. A reference, never a copy.';
COMMENT ON COLUMN tables.content    IS 'Same JSON shape as a table block''s content, so the editor reads it unchanged.';
COMMENT ON COLUMN tables.updated_at IS 'Tables have their own recency; blocks never did.';
