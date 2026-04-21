using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// An earlier revision of 20260421105112_AddImportReviewRedesignColumns
    /// landed in prod with PascalCase column names (TabProgress,
    /// SourceFormat, LastFocusedTab, SourceFile, SourceRange) before we
    /// rewrote the file to use snake_case. Because __EFMigrationsHistory
    /// records the migration as applied, editing the file in place was a
    /// no-op for prod — queries kept hitting 42703 "column 'tab_progress'
    /// does not exist".
    ///
    /// This rename migration reconciles prod: every column that ended up
    /// PascalCase gets moved to the snake_case name the runtime SQL and
    /// HasColumnName mappings agree on. Guarded with pg_attribute checks
    /// so the migration is a no-op on databases that were set up after
    /// the file rewrite (dev / local / fresh).
    /// </summary>
    public partial class RenameRedesignColumnsToSnakeCase : Migration
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
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentional no-op. The rename is a one-way reconcile — the
            // code+snapshot only ever expects the snake_case names going
            // forward, so a PascalCase restoration would break the app.
        }
    }
}
