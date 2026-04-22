using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Coverage honesty pass 1 — stop the catalog from lying about what
    /// the LaTeX parser actually handles. Audit ran 2026-04-22 against
    /// <c>LatexParser.cs</c>'s hardcoded handler sets (KnownEnvironments,
    /// TheoremEnvironments, PassThroughEnvironments, algorithmic regex,
    /// PreservedInlineCommands, MarkdownInlineWrappers, section regex,
    /// citation regex, MatchBalanced metadata extraction). Rows listed
    /// here claimed 'full' but had no specific parser handler — demoted
    /// to 'partial' with a note documenting the actual behaviour.
    ///
    /// Three rows (tabularx × 2, rSection) kept at 'full' but annotated
    /// with the shim path they ride through (commit 9afe55c) so the next
    /// audit doesn't re-flag them.
    ///
    /// Idempotent — every UPDATE is guarded by the current
    /// coverage_level so re-runs are no-ops.
    ///
    /// Next passes (not this migration):
    ///   • Promote the 242 under-claimed rows (algorithmic internals,
    ///     inline-preserved citations/refs, math via KaTeX) once each
    ///     handler category is validation-tested.
    ///   • Parser refactor to dispatch from the catalog (remove the
    ///     hardcoded sets). Covered by project_latex_coverage_pipeline.
    /// </summary>
    public partial class CoverageHonestyPass1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- Vertical-space commands: kernel but no parser handler — silently dropped.
UPDATE latex_tokens
   SET coverage_level = 'partial',
       notes = 'Vertical space directive: stripped on import; surrounding content is untouched.',
       updated_at = NOW()
 WHERE coverage_level = 'full'
   AND kind = 'command'
   AND name IN ('bigskip','medskip','smallskip');

-- Sectioning commands not in the parser section regex
-- (section|subsection|subsubsection|paragraph|subparagraph). The arg
-- text survives via the inline catch-all; heading level is lost.
UPDATE latex_tokens
   SET coverage_level = 'partial',
       notes = 'Heading level not mapped: parser section regex does not include this command. Argument text survives via the inline catch-all; heading hierarchy is lost.',
       updated_at = NOW()
 WHERE coverage_level = 'full'
   AND kind = 'command'
   AND name IN ('chapter','part');

-- Page-break commands: dropped on import.
UPDATE latex_tokens
   SET coverage_level = 'partial',
       notes = 'Page-break directive: dropped on import; Lilia block model does not model pagination.',
       updated_at = NOW()
 WHERE coverage_level = 'full'
   AND kind = 'command'
   AND name = 'pagebreak';

-- Font-family variants not in MarkdownInlineWrappers (textbf/textit/
-- emph/underline). Content survives via catch-all arg extraction but
-- the variant itself is not rendered.
UPDATE latex_tokens
   SET coverage_level = 'partial',
       notes = 'Font-family variant: content preserved via inline catch-all; font family (roman/sans/slanted) not rendered.',
       updated_at = NOW()
 WHERE coverage_level = 'full'
   AND kind = 'command'
   AND name IN ('textrm','textsf','textsl');

-- biblatex output command: parser recognises thebibliography env but
-- not biblatex's \printbibliography entry point.
UPDATE latex_tokens
   SET coverage_level = 'partial',
       notes = 'biblatex-only: parser recognises the kernel thebibliography env, not the biblatex pipeline. Emits an empty bibliography block at this location.',
       updated_at = NOW()
 WHERE coverage_level = 'full'
   AND kind = 'command'
   AND name = 'printbibliography';

-- Starred math environments: parser matches exact env names only, not
-- the *-suffix. Fall through to unknown-env passthrough, which preserves
-- source but does not render as math in the editor preview.
UPDATE latex_tokens
   SET coverage_level = 'partial',
       notes = 'Starred variant: parser matches exact env names, not *-suffix. Content preserved via unknown-env passthrough; math is not rendered in the editor preview.',
       updated_at = NOW()
 WHERE coverage_level = 'full'
   AND kind = 'environment'
   AND name IN ('align*','gather*');

-- Plain 'matrix' env: parser recognises pmatrix/bmatrix/vmatrix/Vmatrix/
-- smallmatrix (KaTeX-rendered inside equation blocks) but not the base
-- amsmath 'matrix' name.
UPDATE latex_tokens
   SET coverage_level = 'partial',
       notes = 'Plain matrix env: parser recognises pmatrix/bmatrix/vmatrix/Vmatrix/smallmatrix but not the base matrix name. Falls through to passthrough.',
       updated_at = NOW()
 WHERE coverage_level = 'full'
   AND kind = 'environment'
   AND name = 'matrix';

-- Beamer frame: parser warns that beamer is not fully supported and
-- imports frames as sections with overlays flattened. That's partial
-- handling, not full.
UPDATE latex_tokens
   SET coverage_level = 'partial',
       notes = 'Beamer frame: imported as a section with overlays (pause/only/onslide) flattened. Slide transitions and layout are not preserved.',
       updated_at = NOW()
 WHERE coverage_level = 'full'
   AND kind = 'environment'
   AND name = 'frame';

-- Annotate the shim-backed rows so the next audit doesn't re-flag them.
-- tabularx: rewritten to \begin{tabular}{cols} at preamble-normalisation
-- time; downstream handling uses the kernel tabular path. (commit 9afe55c)
UPDATE latex_tokens
   SET notes = 'Shim: parse-time normaliser (commit 9afe55c) rewrites tabularx to the kernel tabular env before dispatch. Downstream uses the tabular handler.',
       updated_at = NOW()
 WHERE coverage_level = 'full'
   AND kind = 'environment'
   AND name = 'tabularx';

-- rSection: same shim treatment — rewritten to \section.
UPDATE latex_tokens
   SET notes = 'Shim: parse-time normaliser (commit 9afe55c) rewrites rSection to the kernel \section command before dispatch.',
       updated_at = NOW()
 WHERE coverage_level = 'full'
   AND kind = 'environment'
   AND name = 'rSection';

-- linewidth: keeps 'full' because it's consumed as a length argument to
-- \includegraphics[width=\linewidth]. No standalone handler, but users
-- never use it standalone either — annotate the implicit path.
UPDATE latex_tokens
   SET notes = 'Length-unit token consumed by \includegraphics[width=...] argument parsing. No standalone handler; not meaningful outside width/height contexts.',
       updated_at = NOW()
 WHERE coverage_level = 'full'
   AND kind = 'command'
   AND name = 'linewidth';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentional no-op: we never want to re-establish over-claiming
            // coverage_level values. Promoting back to 'full' without a
            // validated handler is exactly what this migration fixes.
        }
    }
}
