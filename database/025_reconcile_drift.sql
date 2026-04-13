-- 025_reconcile_drift.sql
-- Phase 2 schema reconciliation: brings the prod database in line with the EF
-- model snapshot captured by 20260413055745_ReconcileSchemaDrift, without
-- dropping or altering anything that holds real data.
--
-- Verified safe against prod (DO db-s-1vcpu-1gb) on 2026-04-13:
--   * tenants            — 0 rows, dead stub
--   * jobs.input_file_name, jobs.output_file_name — 0 non-null, dead columns
--   * NULL counts on the targeted timestamp/boolean columns — 0
--   * audit_logs / blocks.path lengths — well under EF varchar caps
--   * block_previews (6565), studio_sessions (319) — TZ conversion is
--     lossless with an explicit USING clause assuming UTC
--   * templates table is missing in prod (was migrated to documents.is_template
--     by 016_templates_as_documents), but the EF model still has the entity.
--     Re-create it empty so the EF model snapshot is consistent. Cleanup of
--     the dead C# entity is a separate follow-up.
--
-- Idempotent: every operation uses IF EXISTS / IF NOT EXISTS so re-runs are
-- no-ops. Wrapped in a single transaction so partial failures roll back.

BEGIN;

-- =====================================================================
-- 1. Drop dead tables
-- =====================================================================

DROP TABLE IF EXISTS public.tenants;

-- =====================================================================
-- 2. Drop dead columns
-- =====================================================================

ALTER TABLE public.jobs DROP COLUMN IF EXISTS input_file_name;
ALTER TABLE public.jobs DROP COLUMN IF EXISTS output_file_name;

-- =====================================================================
-- 3. Convert timestamp without time zone -> with time zone
-- =====================================================================

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'block_previews' AND column_name = 'rendered_at'
          AND data_type = 'timestamp without time zone'
    ) THEN
        ALTER TABLE public.block_previews
            ALTER COLUMN rendered_at TYPE timestamp with time zone
            USING (rendered_at AT TIME ZONE 'UTC');
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'document_pending_invites' AND column_name = 'created_at'
          AND data_type = 'timestamp without time zone'
    ) THEN
        ALTER TABLE public.document_pending_invites
            ALTER COLUMN created_at TYPE timestamp with time zone
            USING (created_at AT TIME ZONE 'UTC');
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'document_pending_invites' AND column_name = 'expires_at'
          AND data_type = 'timestamp without time zone'
    ) THEN
        ALTER TABLE public.document_pending_invites
            ALTER COLUMN expires_at TYPE timestamp with time zone
            USING (expires_at AT TIME ZONE 'UTC');
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'notifications' AND column_name = 'created_at'
          AND data_type = 'timestamp without time zone'
    ) THEN
        ALTER TABLE public.notifications
            ALTER COLUMN created_at TYPE timestamp with time zone
            USING (created_at AT TIME ZONE 'UTC');
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'studio_sessions' AND column_name = 'last_accessed'
          AND data_type = 'timestamp without time zone'
    ) THEN
        ALTER TABLE public.studio_sessions
            ALTER COLUMN last_accessed TYPE timestamp with time zone
            USING (last_accessed AT TIME ZONE 'UTC');
    END IF;
END $$;

-- =====================================================================
-- 4. Tighten varchar widths where prod is looser than the EF model
-- =====================================================================

-- audit_logs: prod uses text on most columns; max lengths are far under the
-- EF varchar caps (verified on 2026-04-13). Tightening is safe.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'audit_logs' AND column_name = 'action' AND data_type = 'text'
    ) THEN
        ALTER TABLE public.audit_logs ALTER COLUMN action TYPE character varying(100);
    END IF;
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'audit_logs' AND column_name = 'entity_type' AND data_type = 'text'
    ) THEN
        ALTER TABLE public.audit_logs ALTER COLUMN entity_type TYPE character varying(100);
    END IF;
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'audit_logs' AND column_name = 'entity_id' AND data_type = 'text'
    ) THEN
        ALTER TABLE public.audit_logs ALTER COLUMN entity_id TYPE character varying(255);
    END IF;
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'audit_logs' AND column_name = 'user_id' AND data_type = 'text'
    ) THEN
        ALTER TABLE public.audit_logs ALTER COLUMN user_id TYPE character varying(255);
    END IF;
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'audit_logs' AND column_name = 'ip_address' AND data_type = 'text'
    ) THEN
        ALTER TABLE public.audit_logs ALTER COLUMN ip_address TYPE character varying(45);
    END IF;
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'audit_logs' AND column_name = 'user_agent' AND data_type = 'text'
    ) THEN
        ALTER TABLE public.audit_logs ALTER COLUMN user_agent TYPE character varying(500);
    END IF;
END $$;

-- blocks.path: prod stores text, max length 4. EF model wants varchar(500).
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'blocks' AND column_name = 'path' AND data_type = 'text'
    ) THEN
        ALTER TABLE public.blocks ALTER COLUMN path TYPE character varying(500);
    END IF;

    -- accounts.scope: prod has varchar(500), EF wants text. Loosen.
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'accounts' AND column_name = 'scope'
          AND character_maximum_length = 500
    ) THEN
        ALTER TABLE public.accounts ALTER COLUMN scope TYPE text;
    END IF;

    -- documents.page_numbering: prod text, EF varchar(20). Max len 6.
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'documents' AND column_name = 'page_numbering' AND data_type = 'text'
    ) THEN
        ALTER TABLE public.documents ALTER COLUMN page_numbering TYPE character varying(20);
    END IF;

    -- jobs.job_type: prod varchar(50), EF varchar(20). Max len 6.
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'jobs' AND column_name = 'job_type' AND character_maximum_length = 50
    ) THEN
        ALTER TABLE public.jobs ALTER COLUMN job_type TYPE character varying(20);
    END IF;
END $$;

-- =====================================================================
-- 5. Tighten NULL → NOT NULL where prod data is verified non-null
-- =====================================================================
-- Sampled prod for NULL counts on these columns and got 0 across the board.
-- EF model defines them NOT NULL with `DEFAULT now()` / `DEFAULT false` /
-- `DEFAULT 0`, so future inserts always have a value.

ALTER TABLE public.accounts                 ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE public.ai_chats                 ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE public.assets                   ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE public.bibliography_entries     ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE public.blocks                   ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE public.blocks                   ALTER COLUMN depth SET NOT NULL, ALTER COLUMN status SET NOT NULL;
ALTER TABLE public.comment_replies          ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE public.comments                 ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE public.conversion_audits        ALTER COLUMN "timestamp" SET NOT NULL;
ALTER TABLE public.document_collaborators   ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE public.document_groups          ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE public.document_snapshots       ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE public.document_versions        ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE public.documents                ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE public.documents                ALTER COLUMN font_family SET NOT NULL, ALTER COLUMN font_size SET NOT NULL,
                                            ALTER COLUMN language SET NOT NULL, ALTER COLUMN paper_size SET NOT NULL;
ALTER TABLE public.documents                ALTER COLUMN is_public SET NOT NULL,
                                            ALTER COLUMN is_template SET NOT NULL,
                                            ALTER COLUMN is_public_template SET NOT NULL,
                                            ALTER COLUMN template_usage_count SET NOT NULL;
ALTER TABLE public.feedback                 ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE public.formulas                 ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE public.group_members            ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE public.groups                   ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN is_default SET NOT NULL;
ALTER TABLE public.import_block_comments    ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE public.import_review_activities ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE public.import_review_collaborators ALTER COLUMN invited_at SET NOT NULL;
ALTER TABLE public.import_review_sessions   ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE public.invitations              ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE public.jobs                     ALTER COLUMN created_at SET NOT NULL,
                                            ALTER COLUMN updated_at SET NOT NULL,
                                            ALTER COLUMN user_id SET NOT NULL;
ALTER TABLE public.jobs                     ALTER COLUMN max_retries SET NOT NULL,
                                            ALTER COLUMN progress SET NOT NULL,
                                            ALTER COLUMN retry_count SET NOT NULL;
ALTER TABLE public.labels                   ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE public.organization_members     ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE public.organizations            ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE public.purchases                ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE public.sessions                 ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE public.snippets                 ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE public.sync_history             ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE public.teams                    ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE public.user_preferences         ALTER COLUMN updated_at SET NOT NULL,
                                            ALTER COLUMN auto_save_enabled SET NOT NULL,
                                            ALTER COLUMN auto_save_interval SET NOT NULL,
                                            ALTER COLUMN keyboard_shortcuts SET NOT NULL,
                                            ALTER COLUMN theme SET NOT NULL;
ALTER TABLE public.users                    ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE public.users                    ALTER COLUMN banned SET NOT NULL, ALTER COLUMN two_factor_enabled SET NOT NULL;
ALTER TABLE public.verifications            ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE public.passkeys                 ALTER COLUMN backed_up SET NOT NULL;

-- blocks.metadata: EF expects NOT NULL with default '{}'::jsonb. Backfill any
-- nulls before tightening.
UPDATE public.blocks SET metadata = '{}'::jsonb WHERE metadata IS NULL;
ALTER TABLE public.blocks ALTER COLUMN metadata SET NOT NULL;

-- studio_sessions array fields: EF expects NOT NULL.
UPDATE public.studio_sessions SET collapsed_ids = '{}'::uuid[] WHERE collapsed_ids IS NULL;
UPDATE public.studio_sessions SET pinned_ids    = '{}'::uuid[] WHERE pinned_ids    IS NULL;
UPDATE public.studio_sessions SET layout        = '{}'::jsonb  WHERE layout        IS NULL;
UPDATE public.studio_sessions SET view_mode     = 'tree'       WHERE view_mode     IS NULL;
ALTER TABLE public.studio_sessions
    ALTER COLUMN collapsed_ids SET NOT NULL,
    ALTER COLUMN pinned_ids    SET NOT NULL,
    ALTER COLUMN layout        SET NOT NULL,
    ALTER COLUMN view_mode     SET NOT NULL;

-- draft_blocks / formulas / snippets jsonb fields with default-only
UPDATE public.draft_blocks SET tags = '[]'::jsonb WHERE tags IS NULL;
ALTER TABLE public.draft_blocks ALTER COLUMN tags SET NOT NULL;

UPDATE public.formulas SET tags = '[]'::jsonb WHERE tags IS NULL;
ALTER TABLE public.formulas ALTER COLUMN tags SET NOT NULL;

UPDATE public.snippets SET tags = '[]'::jsonb WHERE tags IS NULL;
UPDATE public.snippets SET required_packages = '[]'::jsonb WHERE required_packages IS NULL;
ALTER TABLE public.snippets
    ALTER COLUMN tags SET NOT NULL,
    ALTER COLUMN required_packages SET NOT NULL;

-- =====================================================================
-- 6. Re-create empty templates table to satisfy the EF model
-- =====================================================================
-- Templates are now stored as documents WHERE is_template=true (per
-- 016_templates_as_documents.sql), so this table will stay empty going
-- forward. The standalone Template entity in C# is dead code — see task #40
-- for the proper cleanup PR.

CREATE TABLE IF NOT EXISTS public.templates (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id character varying(255),
    name character varying(255) NOT NULL,
    description text,
    category character varying(50),
    thumbnail text,
    content jsonb NOT NULL,
    is_public boolean DEFAULT false NOT NULL,
    is_system boolean DEFAULT false NOT NULL,
    usage_count integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "PK_templates" PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS "IX_templates_user_id"   ON public.templates (user_id);
CREATE INDEX IF NOT EXISTS "IX_templates_category"  ON public.templates (category);
CREATE INDEX IF NOT EXISTS "IX_templates_is_public" ON public.templates (is_public);
CREATE INDEX IF NOT EXISTS "IX_templates_is_system" ON public.templates (is_system);

-- =====================================================================
-- 7. Mark the EF migration as applied
-- =====================================================================
-- The EF-generated migration (20260413055745_ReconcileSchemaDrift) is the
-- baseline that captures everything in the EF model. We don't run its Up()
-- against prod because prod is already in the target state via the
-- database/*.sql files. Mark it as applied so future EF migrations work
-- correctly. __EFMigrationsHistory may not exist yet — create it first.

CREATE TABLE IF NOT EXISTS public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

-- Backfill all historical EF migrations as already-applied (prod was built
-- without ever running any of them — this one statement makes the history
-- table reflect reality).
INSERT INTO public."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES
    ('20260208175702_AddColumnSettings',         '10.0.3'),
    ('20260209151912_AddSnippets',               '10.0.3'),
    ('20260210132846_ConsolidatedSchema',        '10.0.3'),
    ('20260214081447_AddDocumentLayoutFields',   '10.0.3'),
    ('20260215085515_FixDocumentLayoutColumnNames', '10.0.3'),
    ('20260413055745_ReconcileSchemaDrift',      '10.0.3')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;
