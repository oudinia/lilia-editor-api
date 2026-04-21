using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Belt-and-suspenders for the redesign-columns saga. The previous
    /// 20260421152200_RenameRedesignColumnsToSnakeCase ran successfully in
    /// prod but didn't find the PascalCase columns to rename and also
    /// didn't create the snake_case ones — meaning prod still has no
    /// tab_progress / last_focused_tab / source_format columns despite
    /// EFMigrationsHistory claiming the redesign migration is applied.
    ///
    /// This migration idempotently creates every snake_case column the
    /// runtime depends on (ADD COLUMN IF NOT EXISTS). Harmless no-op on
    /// environments where the columns already exist.
    /// </summary>
    public partial class EnsureRedesignColumnsExist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE public.import_review_sessions
  ADD COLUMN IF NOT EXISTS tab_progress     jsonb,
  ADD COLUMN IF NOT EXISTS last_focused_tab text,
  ADD COLUMN IF NOT EXISTS source_format    text NOT NULL DEFAULT 'tex';

ALTER TABLE public.import_block_reviews
  ADD COLUMN IF NOT EXISTS source_file  text,
  ADD COLUMN IF NOT EXISTS source_range jsonb;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op. The columns are load-bearing for the redesigned review
            // pipeline; dropping them in a Down() would break the service.
        }
    }
}
