using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Coverage honesty pass 2 — promote 26 under-claimed rows whose
    /// handlers are provably in LatexParser.cs. Complements pass 1
    /// (20260422212949_CoverageHonestyPass1) which demoted over-claims.
    ///
    /// Each batch maps to a specific parser handler surface. The audit
    /// at /tmp/coverage_audit.py (2026-04-22) classified these rows as
    /// "under-claimed" — auto-inserted as unsupported by the parser's
    /// catalog-token scanner, but the parser does have concrete handling
    /// for them elsewhere.
    ///
    ///   batch A — algorithmic internals (ParseAlgorithmicLines regex)
    ///   batch B — PreservedInlineCommands (kept verbatim for downstream)
    ///   batch C — citation regex (inline citation pattern)
    ///   batch D — inline-code commands (backtick-wrapped)
    ///   batch E — metadata-extract (MatchBalanced + StripBalancedCommand)
    ///
    /// Idempotent: each UPDATE is guarded by `coverage_level = 'unsupported'`.
    /// </summary>
    public partial class CoverageHonestyPass2Promotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- =====================================================================
-- Batch A — algorithmic internals (12 rows)
--
-- LatexParser.ParseAlgorithmicLines walks algorithm / algorithmic /
-- algorithm2e env bodies with a case-insensitive regex. Each match
-- becomes a typed ImportAlgorithmLine (kind = statement / if / elsif /
-- else / endif / for / endfor / while / endwhile / require / ensure /
-- return). First-class handling → 'full'.
-- =====================================================================
UPDATE latex_tokens
   SET coverage_level = 'full',
       maps_to_block_type = 'algorithm',
       notes = 'Parsed by LatexParser.ParseAlgorithmicLines into a typed ImportAlgorithmLine inside the enclosing algorithm block. Case-insensitive match against the algorithmic command regex.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND name IN (
     'If', 'ElsIf', 'Else', 'EndIf',
     'For', 'EndFor',
     'While', 'EndWhile',
     'Require', 'Ensure', 'Return',
     'State'
   );

-- =====================================================================
-- Batch B — PreservedInlineCommands (4 rows)
--
-- LatexParser.NormaliseInlineCommands keeps these verbatim so the
-- downstream renderer can resolve them (cref/Cref link out, citeauthor/
-- citeyear resolve to bibliography names, href renders as a link).
-- =====================================================================
UPDATE latex_tokens
   SET coverage_level = 'full',
       notes = 'Preserved verbatim by NormaliseInlineCommands; downstream renderer resolves the reference / link.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND name IN ('Cref', 'citeauthor', 'citeyear', 'href');

-- url is slightly different: preserved AND rendered as a link by the
-- editor. Same full, different note.
UPDATE latex_tokens
   SET coverage_level = 'full',
       maps_to_block_type = NULL,
       notes = 'Preserved verbatim by NormaliseInlineCommands; rendered as an inline link in the editor.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND name = 'url';

-- =====================================================================
-- Batch C — citation regex (6 rows)
--
-- LatexParser citePattern matches cite | citep | citet | citealp |
-- citealt | parencite | textcite | footcite | autocite | nocite.
-- Body keys are split on commas, each key becomes a resolved citation
-- in the output paragraph. 'autocite' / 'parencite' / 'textcite' also
-- appear in the catalog at biblatex scope as 'full'; these are the
-- kernel-scope dupes auto-inserted by the scanner.
-- =====================================================================
UPDATE latex_tokens
   SET coverage_level = 'full',
       notes = 'Matched by LatexParser.citePattern; body keys split on commas and resolved against the bibliography. Duplicate of the biblatex/natbib-scoped catalog row.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND name IN ('autocite', 'citep', 'citet', 'nocite', 'parencite', 'textcite');

-- =====================================================================
-- Batch D — inline-code commands (1 row)
--
-- CodeDisplayInlineCommands wraps the argument in backticks so LaTeX-
-- about-LaTeX docs keep the monospace visual. \path is less common but
-- part of the same set.
-- =====================================================================
UPDATE latex_tokens
   SET coverage_level = 'full',
       notes = 'CodeDisplayInlineCommands: argument wrapped in Markdown backticks at import time.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND name = 'path';

-- =====================================================================
-- Batch E — metadata-extract (2 rows)
--
-- Parser runs MatchBalanced / StripBalancedCommand against 'title',
-- 'author', 'date', 'caption', 'thanks', 'affil', 'affiliation'.
-- These commands contribute to document metadata and are stripped
-- from the body so they don't leak into a paragraph. 'thanks' and
-- 'affiliation' are the under-claimed ones; the others are already
-- at 'full' in the catalog.
-- =====================================================================
UPDATE latex_tokens
   SET coverage_level = 'full',
       notes = 'Stripped from body by LatexParser.StripBalancedCommand; argument contributes to document metadata (author affiliation / funding acknowledgement).',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND name IN ('affiliation', 'thanks');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentional no-op: these rows have validated parser handlers;
            // demoting them back to 'unsupported' is exactly what pass 2 fixes.
        }
    }
}
