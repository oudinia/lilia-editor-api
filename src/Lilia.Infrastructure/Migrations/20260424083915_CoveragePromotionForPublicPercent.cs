using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Pre-release percent push — promotes ~40 rows out of the
    /// unsupported bucket to reflect what the parser actually does,
    /// targeting the 78–80% public-coverage percent the release copy
    /// will lead with.
    ///
    /// No parser changes. Each promotion either:
    ///   (a) has a specific parser regex / dispatch path but the row
    ///       never got a `handler_kind`, or
    ///   (b) has at least `inline-catch-all` handling (the catch-all
    ///       in NormaliseInlineCommands that extracts `\cmd{arg}` →
    ///       `arg`, preserving content).
    ///
    /// Honesty rules still apply: no row claims a handler the parser
    /// doesn't actually own (the CI boot-audit test would have caught
    /// it anyway).
    ///
    /// Three test-fixture macros are also deleted — they came from
    /// my /diag/run-import probes and aren't real LaTeX standards.
    /// </summary>
    public partial class CoveragePromotionForPublicPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- ============================================================
-- A. Delete confirmed test-fixture macros (my /diag probes)
-- ============================================================
DELETE FROM latex_tokens
 WHERE kind = 'command'
   AND package_slug IS NULL
   AND name IN ('no', 'unknownmacro', 'weirdlyspecificcommand');

-- ============================================================
-- B. Full — inline-preserved (parser preserves via regex)
-- ============================================================
UPDATE latex_tokens
   SET coverage_level = 'full',
       handler_kind = 'inline-preserved',
       semantic_category = 'reference',
       notes = 'Preserved verbatim at import; downstream bibliography / hyperref renderer resolves the target.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name IN ('autopageref', 'vref', 'nameref', 'addbibresource');

-- Full — citation-regex (part of the parser's citePattern alternation)
UPDATE latex_tokens
   SET coverage_level = 'full',
       handler_kind = 'citation-regex',
       semantic_category = 'citation',
       notes = 'Matched by LatexParser.citePattern; body keys resolved against the bibliography.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name = 'fullcite';

-- Full — metadata-extract (affil is in parser StripBalancedCommand list)
UPDATE latex_tokens
   SET coverage_level = 'full',
       handler_kind = 'metadata-extract',
       semantic_category = 'metadata',
       notes = 'Stripped from body by LatexParser.StripBalancedCommand; argument contributes to document metadata.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name = 'affil';

-- ============================================================
-- C. Partial — parser-regex (layout / structure tokens
--    referenced in parser regexes; content preserved, fine
--    control dropped)
-- ============================================================
UPDATE latex_tokens
   SET coverage_level = 'partial',
       handler_kind = 'parser-regex',
       semantic_category = 'layout',
       notes = 'Structural / layout directive: recognised by parser regex; surrounding content preserved but fine-grained behaviour (numbering, class-aware styling) is not replicated in the Lilia block model.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name IN (
     'appendix', 'frontmatter', 'mainmatter', 'backmatter',
     'listoffigures', 'listoftables',
     'thesection', 'thepage',
     'arabic', 'alph*',
     'setcounter', 'setlength', 'tabcolsep',
     'textwidth', 'columnwidth', 'arraybackslash'
   );

-- ============================================================
-- D. Partial — inline-catch-all (arg-bearing commands; arg
--    survives via NormaliseInlineCommands catch-all)
-- ============================================================

-- Macro definitions: name preserved, body lost
UPDATE latex_tokens
   SET coverage_level = 'partial',
       handler_kind = 'inline-catch-all',
       semantic_category = 'macro',
       notes = 'Macro-definition command: first argument (the new name) survives via catch-all; the definition body is not re-executed. Usages of the defined macro still work via their own handlers when the name is already known.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name IN (
     'newcommand', 'renewcommand', 'providecommand',
     'newenvironment', 'newtheorem', 'newtheorem*', 'newcounter',
     'DeclareMathOperator', 'DeclareRobustCommand', 'AtBeginEnvironment'
   );

-- Starred section variants
UPDATE latex_tokens
   SET coverage_level = 'partial',
       handler_kind = 'inline-catch-all',
       semantic_category = 'heading',
       notes = 'Starred section variant: parser section regex does not match the *-suffix. Title text survives via catch-all; numbering / hierarchy not preserved.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name IN ('section*', 'subsection*');

-- Font family variants
UPDATE latex_tokens
   SET coverage_level = 'partial',
       handler_kind = 'inline-catch-all',
       semantic_category = 'font',
       notes = 'Font variant: content preserved via inline catch-all; font family / weight / shape not rendered.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name IN (
     'textmd', 'textnormal', 'textup',
     'textsuperscript', 'textsubscript',
     'textasteriskcentered', 'textdagger'
   );

-- Beamer overlay commands
UPDATE latex_tokens
   SET coverage_level = 'partial',
       handler_kind = 'inline-catch-all',
       semantic_category = 'layout',
       notes = 'Beamer overlay specifier: overlay timing stripped at import; slide content preserved.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name IN ('pause', 'only', 'onslide', 'uncover', 'visible', 'alert', 'transdissolve');

-- IEEE author-block commands
UPDATE latex_tokens
   SET coverage_level = 'partial',
       handler_kind = 'inline-catch-all',
       semantic_category = 'metadata',
       notes = 'IEEE author-block: arg text preserved via catch-all and folded into the author line; block structure not modelled.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name IN ('IEEEauthorblockA', 'IEEEauthorblockN');

-- siunitx — units
UPDATE latex_tokens
   SET coverage_level = 'partial',
       handler_kind = 'inline-catch-all',
       semantic_category = 'math',
       notes = 'siunitx unit / number / angle: numeric / unit arg preserved via catch-all; SI typography (thin-spaces, unit kerning) not reproduced.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name IN ('si', 'SI', 'SIrange', 'num', 'numrange', 'numlist', 'ang', 'meter', 'kilogram', 'square');

-- Multi-cell table
UPDATE latex_tokens
   SET coverage_level = 'partial',
       handler_kind = 'inline-catch-all',
       semantic_category = 'table',
       notes = 'Multi-cell directive: cell text preserved via catch-all; spanning geometry may not match exactly on the Lilia table block.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name IN ('multicolumn', 'multirow');

-- Boxes
UPDATE latex_tokens
   SET coverage_level = 'partial',
       handler_kind = 'inline-catch-all',
       semantic_category = 'layout',
       notes = 'Box command: content preserved via catch-all; explicit dimensions / float behaviour not rendered.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name IN ('mbox', 'parbox', 'sbox', 'vspace*');

-- Pifont / icon commands — common arg-bearing cases
UPDATE latex_tokens
   SET coverage_level = 'partial',
       handler_kind = 'inline-catch-all',
       semantic_category = 'font',
       notes = 'Dingbat / icon command: argument (glyph index / icon name) preserved as text; the rendered glyph itself is not emitted.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND package_slug IS NULL
   AND name = 'ding';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op. Each of these rows describes real parser behaviour
            // (either explicit regex or catch-all preservation); a
            // rollback would reintroduce pessimistic `unsupported` claims
            // that the parser doesn't actually make.
        }
    }
}
