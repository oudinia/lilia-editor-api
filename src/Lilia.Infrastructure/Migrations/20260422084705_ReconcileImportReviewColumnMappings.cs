using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Reconciles the PascalCase → snake_case column drift on
    /// import_review_sessions and import_block_reviews. An earlier rewrite of
    /// 20260421105112_AddImportReviewRedesignColumns landed two follow-ups
    /// (RenameRedesignColumnsToSnakeCase, EnsureRedesignColumnsExist) that
    /// shipped without .Designer.cs files and were silently skipped by
    /// Database.MigrateAsync(). Prod was repaired by hand on 2026-04-22; this
    /// migration captures that repair as a first-class, registered migration
    /// so fresh databases and any still-drifted environments converge.
    ///
    /// Idempotent on every path:
    ///   - prod (already snake_case)     guards skip, ALTER re-asserts default
    ///   - fresh DB (already snake_case) guards skip, ALTER re-asserts default
    ///   - drifted DB (still PascalCase) renames fire, ALTER sets default
    /// </summary>
    public partial class ReconcileImportReviewColumnMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = 'public.import_review_sessions'::regclass AND attname = 'TabProgress') THEN
    ALTER TABLE public.import_review_sessions RENAME COLUMN ""TabProgress"" TO tab_progress;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = 'public.import_review_sessions'::regclass AND attname = 'SourceFormat') THEN
    ALTER TABLE public.import_review_sessions RENAME COLUMN ""SourceFormat"" TO source_format;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = 'public.import_review_sessions'::regclass AND attname = 'LastFocusedTab') THEN
    ALTER TABLE public.import_review_sessions RENAME COLUMN ""LastFocusedTab"" TO last_focused_tab;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = 'public.import_block_reviews'::regclass AND attname = 'SourceFile') THEN
    ALTER TABLE public.import_block_reviews RENAME COLUMN ""SourceFile"" TO source_file;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = 'public.import_block_reviews'::regclass AND attname = 'SourceRange') THEN
    ALTER TABLE public.import_block_reviews RENAME COLUMN ""SourceRange"" TO source_range;
  END IF;
END $$;

ALTER TABLE public.import_review_sessions
  ADD COLUMN IF NOT EXISTS tab_progress     jsonb,
  ADD COLUMN IF NOT EXISTS last_focused_tab text,
  ADD COLUMN IF NOT EXISTS source_format    text NOT NULL DEFAULT 'tex';

ALTER TABLE public.import_block_reviews
  ADD COLUMN IF NOT EXISTS source_file  text,
  ADD COLUMN IF NOT EXISTS source_range jsonb;

ALTER TABLE public.import_review_sessions ALTER COLUMN source_format SET DEFAULT 'tex';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op — one-way reconcile. Snapshot and runtime only expect snake_case.
        }
    }
}
