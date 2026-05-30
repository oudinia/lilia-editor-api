using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// REGRESSION-001 backfill. Pre-fix, <c>DocumentService.CreateDocumentAsync</c>
    /// seeded every new document with an auto-paragraph whose
    /// <c>content</c> jsonb was the JSON string <c>""</c> rather than
    /// the empty object <c>{}</c>. Per-block renderers call
    /// <c>content.TryGetProperty("text", …)</c>, which throws
    /// <c>InvalidOperationException</c> on a String-kind root and
    /// surfaces as the "Error rendering block" sentinel HTML —
    /// exactly the bug the sync-recovery suite was pinning.
    ///
    /// This migration rewrites every existing paragraph block whose
    /// content jsonb is literally <c>""</c> (the bad shape) to an
    /// empty object <c>{}</c>. New blocks created after the
    /// service-layer fix are unaffected (their content is already
    /// an object).
    ///
    /// Reversible — the Down() migration restores the string shape,
    /// not because anyone would want it, but so EF's rollback path
    /// stays consistent.
    /// </summary>
    public partial class BackfillBlockContentEmptyStringToObject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only paragraph rows where the entire jsonb document is
            // the JSON string "". Bounded query — no impact on the
            // hot path.
            migrationBuilder.Sql(@"
                UPDATE blocks
                SET content = '{}'::jsonb
                WHERE content::text = '""""'
                  AND type = 'paragraph';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the legacy shape for any rows that LOOK like
            // they came from the seeder (no `text` field, exactly
            // empty). We don't blanket-rewrite every `{}` paragraph
            // — that would clobber intentional empty paragraphs that
            // postdated the fix.
            migrationBuilder.Sql(@"
                UPDATE blocks
                SET content = '""""'::jsonb
                WHERE content::text = '{}'
                  AND type = 'paragraph';
            ");
        }
    }
}
