using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// align* and gather* back to full, with a note that says what is true.
    ///
    /// <para>They were demoted on 22 April with the note "parser matches exact
    /// env names, not *-suffix", which was accurate that day: a starred
    /// environment fell through to passthrough and kept its source verbatim.
    /// Five days later the parser gained a \*? that fixed the matching and put the
    /// star outside the capture group — so from 27 April every align* was matched
    /// and re-wrapped as a numbered align. The note never changed. It described a
    /// harmless gap for five months while the code did something worse: it
    /// silently numbered the author's unnumbered equations on every export.</para>
    ///
    /// <para>The star is now carried as <c>numbered: false</c> on the block, which
    /// the renderer turns back into the star. Round trip covered by
    /// LatexParserStarredMathTests.</para>
    /// </summary>
    public partial class CorrectStarredMathCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE latex_tokens
   SET coverage_level = 'full',
       handler_kind   = 'math-env',
       notes          = 'Starred variant. The star is carried as numbered:false on the equation block and emitted again at render, so it round-trips and follows the author''s numbering toggle.',
       updated_at     = NOW()
 WHERE kind = 'environment'
   AND name IN ('align*', 'gather*');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE latex_tokens
   SET coverage_level = 'partial',
       notes          = 'Starred variant: parser matches exact env names, not *-suffix. Content preserved via unknown-env passthrough; math is not rendered in the editor preview.',
       updated_at     = NOW()
 WHERE kind = 'environment'
   AND name IN ('align*', 'gather*');
");
        }
    }
}
