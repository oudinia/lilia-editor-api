using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Backfills equation blocks from the legacy <c>{ latex }</c> shape to
    /// <c>{ notation, source }</c>.
    ///
    /// New writes already produce both — <c>BlockContentNormaliser</c> sees to
    /// that — so this is only for rows authored before it landed. Until they are
    /// backfilled, the readers' fallback to <c>latex</c> is load-bearing and the
    /// mirror cannot be dropped; afterwards, dropping both is a separate step
    /// that should be taken only once this has run everywhere.
    ///
    /// No schema change: block content is jsonb and the new keys are additive,
    /// which is why this is a data-only migration.
    /// </summary>
    public partial class BackfillEquationSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent by construction: the WHERE clause excludes rows that
            // already carry a non-empty `source`, so re-running changes nothing.
            //
            // `latex` is deliberately left in place rather than moved. Anything
            // built against the old shape — an export on disk, a client not yet
            // updated — still reads it, and one duplicated string per equation
            // is a small price for not breaking them. Removing it is the step
            // after this one.
            migrationBuilder.Sql("""
                UPDATE blocks
                SET content = content
                    || jsonb_build_object('source', content->>'latex')
                    || jsonb_build_object('notation', COALESCE(content->>'notation', 'latex'))
                WHERE lower(type) = 'equation'
                  AND jsonb_typeof(content) = 'object'
                  AND COALESCE(content->>'latex', '') <> ''
                  AND COALESCE(content->>'source', '') = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op.
            //
            // Reversing this would mean stripping `source` and `notation` from
            // equation blocks — but by the time anyone rolls back, those keys
            // are also being written by normal application traffic, and this
            // migration cannot tell a backfilled row from a freshly authored
            // one. Removing them wholesale would discard current data to undo a
            // historical copy.
            //
            // Nothing needs the reversal anyway: `latex` was never removed, so
            // the pre-migration readers still find exactly what they expect.
        }
    }
}
