using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Coverage honesty pass 4 — final 15 under-claimed rows.
    ///
    /// All 15 of these have explicit parser handling — they are the
    /// last kernel-scope dupes the auto-insert path created when the
    /// 2026-04-22 diag corpus ran. After this, the audit's under-
    /// claimed bucket is 0.
    ///
    ///   commands (6):
    ///     bibitem             — inside thebibliography env; regex preserves
    ///     bibliographystyle   — parser regex strips-and-records the style
    ///     toprule / midrule / bottomrule — booktabs rules; parser regex
    ///                           references retained inside table output
    ///     par                 — paragraph break; parser splits on it
    ///
    ///   environments (9):
    ///     algorithm / algorithmic — ImportAlgorithm dispatch
    ///     align / gather / multline / eqnarray — math env dispatch
    ///     lstlisting          — code block dispatch
    ///     subfigure           — sub-figure dispatch
    ///     verbatim            — code block dispatch
    ///
    ///   All are in LatexParser.KnownEnvironments or the parser's
    ///   command-regex references. These kernel-scope rows are dupes
    ///   of already-full rows under their proper package scope
    ///   (amsmath, listings, subcaption, verbatim, booktabs, natbib,
    ///   kernel). The catalog's Coverage tab will read the first
    ///   matching row, so these dupes at 'unsupported' were causing
    ///   misleading flags for any doc that imported from the kernel
    ///   scope.
    ///
    /// Idempotent — guarded by coverage_level = 'unsupported'.
    /// </summary>
    public partial class CoverageHonestyPass4FinalPromotion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- Commands: parser has explicit regex references (see /tmp/p_raw.txt
-- from the 2026-04-22 audit). Covered in LatexParser.cs, mostly as
-- preserve-and-strip for metadata or inline passthrough.
UPDATE latex_tokens
   SET coverage_level = 'full',
       notes = 'Parser regex preserves and routes: bibitem inside thebibliography env, bibliographystyle strip-and-record, toprule/midrule/bottomrule inline booktabs rules kept inside table output, par honoured as paragraph break.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND name IN ('bibitem','bibliographystyle','toprule','midrule','bottomrule','par');

-- Environments: all in LatexParser.KnownEnvironments. These are
-- kernel-scope dupes of rows already at 'full' under their package
-- scope (amsmath / listings / subcaption / verbatim / appendix /
-- wrapfig / multicol). Align the kernel-scope rows too.
UPDATE latex_tokens
   SET coverage_level = 'full',
       maps_to_block_type = CASE name
         WHEN 'algorithm'   THEN 'algorithm'
         WHEN 'algorithmic' THEN 'algorithm'
         WHEN 'align'       THEN 'equation'
         WHEN 'gather'      THEN 'equation'
         WHEN 'multline'    THEN 'equation'
         WHEN 'eqnarray'    THEN 'equation'
         WHEN 'lstlisting'  THEN 'code'
         WHEN 'subfigure'   THEN 'figure'
         WHEN 'verbatim'    THEN 'code'
         ELSE maps_to_block_type
       END,
       notes = 'Recognised by LatexParser.KnownEnvironments; dispatches to the corresponding import element type. Kernel-scope duplicate of an existing full row under its canonical package scope.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'environment'
   AND name IN ('algorithm','algorithmic','align','gather','multline','eqnarray',
                'lstlisting','subfigure','verbatim');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: we never re-demote validated-handler rows.
        }
    }
}
