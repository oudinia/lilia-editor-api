using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Closes <c>math.two-letter-identifier</c>, and corrects the severity it
    /// was carrying.
    ///
    /// <para>The catalogue rated it <c>info</c> / <c>none</c>. Counting the
    /// telemetry instead: of 34 <c>typst-compile-failed</c> fallbacks, 26 were
    /// this one error and the other 8 were a missing Typst binary. It was not an
    /// <c>info</c> — it was every genuine translation failure in the corpus.</para>
    ///
    /// <para>The row also recorded why nobody had fixed it: <i>"Safe-space-insertion
    /// would risk breaking sin/cos/log/etc. function names."</i> That risk was
    /// real, and is what the fix is built around — the splitter skips known
    /// identifiers, function calls, dotted paths, named arguments, quoted text
    /// and untranslated commands, so the function names it was protecting are
    /// exactly the cases it leaves alone.</para>
    /// </summary>
    public partial class ResolveTypstImplicitProductGap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE typst_translation_gaps
SET mitigation_status = 'shipped',
    blocking_severity = 'warn',
    notes = 'SHIPPED - TypstExportService.SplitImplicitProducts separates adjacent '
         || 'single-letter variables so `mc` emits as `m c`. Severity was ''info'' and '
         || 'that was wrong: 26 of 34 typst-compile-failed telemetry events were this '
         || 'error alone (essentially all `E = mc^2`), the remaining 8 being a missing '
         || 'Typst binary - so it accounted for every genuine translation failure '
         || 'measured, not an edge case. The earlier note worried that space insertion '
         || 'would break sin/cos/log; the splitter therefore skips known identifiers, '
         || 'function calls, dotted paths, named arguments, quoted text and commands '
         || 'that still carry a backslash. Verified against typst 0.15.0: `$ E = mc^2 $` '
         || 'fails with ''unknown variable: mc'', `$ E = m c^2 $` compiles.'
WHERE gap_key = 'math.two-letter-identifier';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the row exactly as the seed migration wrote it, so a
            // rollback leaves the catalogue as it was rather than half-updated.
            migrationBuilder.Sql(@"
UPDATE typst_translation_gaps
SET mitigation_status = 'none',
    blocking_severity = 'info',
    notes = 'Typst math treats `ab` as one identifier; LaTeX as `a*b`. Doc falls back '
         || 'to pdflatex; ~6-8s vs <3s on Typst path. Safe-space-insertion would risk '
         || 'breaking sin/cos/log/etc. function names.'
WHERE gap_key = 'math.two-letter-identifier';
");
        }
    }
}
