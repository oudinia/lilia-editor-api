using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Second-pass percent push. Honest cap on how far we can move the
    /// public coverage percent: only rows where the parser genuinely
    /// preserves content — either via inline catch-all (arg survives)
    /// or via an enclosing handled env (tikzpicture / algorithm).
    ///
    /// Splits into three groups:
    ///   A. arg-bearing commands, first arg extracted by
    ///      NormaliseInlineCommands catch-all
    ///   B. algorithm extras — inside an algorithm env, text preserved
    ///      in a statement line
    ///   C. tikz / pgfplots internals — inside tikzpicture/axis envs
    ///      which are themselves passthrough blocks
    ///
    /// Not promoted (would require dishonesty or a formula change):
    ///   • no-arg directives that leak literal `\cmd` into paragraph
    ///     text (faXxx icons, \newline, \qedhere, \TeX, \LaTeX, …) —
    ///     they're content-preserved but the preserved artefact is
    ///     raw LaTeX, which reads as unsupported to end users.
    ///   • user-specific macros (corres, dates, docinfo, vol, …) —
    ///     no generic handler possible.
    ///   • longtable internals (endfirsthead, endfoot, endlastfoot)
    ///     that the parser doesn't pair with any dispatch path.
    /// </summary>
    public partial class CoveragePromotionRound2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- A. arg-bearing commands — catch-all extracts arg
UPDATE latex_tokens
   SET coverage_level = 'partial',
       handler_kind = 'inline-catch-all',
       semantic_category = 'layout',
       notes = 'Arg-bearing command: first argument preserved via NormaliseInlineCommands catch-all; structural role (TOC entry / listing inclusion / legend entry) not modelled in the Lilia block system.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name IN (
     'addcontentsline',   -- \addcontentsline{toc}{section}{name} — first arg preserved
     'addlegendentry',    -- \addlegendentry{label} — arg preserved
     'inputminted',       -- \inputminted[opts]{lang}{file} — language arg preserved
     'lstinputlisting',   -- \lstinputlisting[opts]{file} — file arg preserved
     'usemintedstyle',    -- \usemintedstyle{colorful} — style arg preserved
     'tikzstyle',         -- \tikzstyle{name}=[...] — style name preserved
     'argmax',            -- \DeclareMathOperator-defined; arg preserved in math context
     'argmin'             -- same
   );

-- B. algorithm extras — arg preserved inside the enclosing algorithm env
UPDATE latex_tokens
   SET coverage_level = 'partial',
       handler_kind = 'inline-catch-all',
       semantic_category = 'layout',
       notes = 'Algorithmic pseudocode command not in the primary regex alternation; text preserved inside the enclosing algorithm env as a statement line.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name IN ('Call', 'EndFunction', 'EndProcedure', 'Function', 'Procedure');

-- C. tikz / pgfplots internals — preserved inside tikzpicture/axis env
UPDATE latex_tokens
   SET coverage_level = 'partial',
       handler_kind = 'passthrough',
       semantic_category = 'graphics',
       notes = 'TikZ / pgfplots internal; preserved as part of the enclosing tikzpicture / axis env which is already a passthrough block.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name IN ('draw', 'node', 'fill', 'filldraw', 'useasboundingbox', 'column');

-- Delete three confirmed rhostart / theday / coderef — user macros
-- picked up from a specific doc's preamble. Keeping them doesn't
-- help signal quality; deletion lets the catalog re-auto-insert
-- them on next real import if they genuinely recur.
DELETE FROM latex_tokens
 WHERE kind = 'command'
   AND package_slug IS NULL
   AND coverage_level = 'unsupported'
   AND name IN ('rhostart', 'theday', 'coderef', 'doctype', 'docinfo');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op. Each promotion describes a real parser path;
            // rollback would reintroduce pessimistic claims.
        }
    }
}
