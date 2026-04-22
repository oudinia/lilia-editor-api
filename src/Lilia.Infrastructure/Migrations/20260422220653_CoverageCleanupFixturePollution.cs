using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Remove fixture-local test macros that the catalog scanner auto-
    /// inserted when the 2026-04-22 coverage-corpus diag fixture ran.
    ///
    /// The fixture deliberately defines user macros (\R, \Z, \mycmd) to
    /// exercise the \newcommand / \DeclareRobustCommand parse paths.
    /// Those macro *names* end up in latex_tokens because the scanner
    /// is agnostic about whether a \cmd is kernel-defined or user-
    /// defined. They don't represent real LaTeX standards and we don't
    /// want them showing up on the public Coverage tab.
    ///
    /// Long-term fix: teach the scanner to skip tokens introduced by
    /// \newcommand / \renewcommand / \providecommand / \DeclareMathOperator
    /// bodies in the same file. Out of scope for this migration.
    ///
    /// Idempotent — no-op if the rows don't exist.
    /// </summary>
    public partial class CoverageCleanupFixturePollution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM latex_tokens
 WHERE kind = 'command'
   AND coverage_level = 'unsupported'
   AND package_slug IS NULL
   AND name IN ('R', 'Z', 'mycmd');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: restoring user-fixture pollution serves no purpose.
        }
    }
}
