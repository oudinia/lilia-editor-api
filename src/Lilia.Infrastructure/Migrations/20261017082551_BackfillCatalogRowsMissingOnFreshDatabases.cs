using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Kernel catalog rows that earlier migrations upgraded only if they already existed.
    ///
    /// <para>UpgradeKernelTokenCoverage, the CoverageHonesty passes, AddTokenHandlerKind and the
    /// insert-template backfills UPDATE rows the parser had auto-inserted (as 'unsupported') on the
    /// databases of the time. On a database created since, those rows do not exist when the
    /// migrations run; the parser inserts them later as 'unsupported' and nothing upgrades them. A
    /// fresh database then reported \documentclass, \usepackage, \frac, the Greek letters and
    /// ~140 more as unsupported, and a plain .tex as about 33% covered (found by the e2e import
    /// report spec, 15 Oct 2026).</para>
    ///
    /// <para>The values below are what those migrations leave: computed by replaying the history
    /// on an empty database with the parser's placeholder rows inserted after SeedLatexCatalog, as
    /// production had them. A row is written only where it is missing, or is still exactly the
    /// parser's placeholder, so a database migrated in order (production) keeps them as they are.</para>
    ///
    /// <para>One exception: six commands (\Huge, \normalsize, \centering, \raggedright,
    /// \raggedleft, \noindent) were promoted to 'shimmed' without a handler_kind on every
    /// database, against CatalogIntegrityTests' contract. They get the handler the parser uses.</para>
    /// </summary>
    public partial class BackfillCatalogRowsMissingOnFreshDatabases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TEMP TABLE catalog_backfill (
  name varchar, kind varchar, arity int, optional_arity int, expects_body boolean,
  semantic_category varchar, maps_to_block_type varchar, coverage_level varchar,
  notes text, handler_kind varchar, insert_template text
) ON COMMIT DROP;

INSERT INTO catalog_backfill VALUES
  ('addbibresource', 'command', NULL, NULL, false, 'reference', NULL, 'full', 'Preserved verbatim at import; downstream bibliography / hyperref renderer resolves the target.', 'inline-preserved', '\addbibresource{|CURSOR|}'),
  ('affil', 'command', NULL, NULL, false, 'metadata', NULL, 'full', 'Stripped from body by LatexParser.StripBalancedCommand; argument contributes to document metadata.', 'metadata-extract', '\affil{|CURSOR|}'),
  ('affiliation', 'command', NULL, NULL, false, NULL, NULL, 'full', 'Stripped from body by LatexParser.StripBalancedCommand; argument contributes to document metadata (author affiliation / funding acknowledgement).', 'metadata-extract', '\affiliation{|CURSOR|}'),
  ('aleph', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('alph*', 'command', NULL, NULL, false, 'layout', NULL, 'partial', 'Structural / layout directive: recognised by parser regex; surrounding content preserved but fine-grained behaviour (numbering, class-aware styling) is not replicated in the Lilia block model.', 'parser-regex', NULL),
  ('alpha', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('appendix', 'command', NULL, NULL, false, 'layout', NULL, 'partial', 'Structural / layout directive: recognised by parser regex; surrounding content preserved but fine-grained behaviour (numbering, class-aware styling) is not replicated in the Lilia block model.', 'parser-regex', '\appendix'),
  ('approx', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('arabic', 'command', NULL, NULL, false, 'layout', NULL, 'partial', 'Structural / layout directive: recognised by parser regex; surrounding content preserved but fine-grained behaviour (numbering, class-aware styling) is not replicated in the Lilia block model.', 'parser-regex', '\arabic{|CURSOR|}'),
  ('array', 'environment', NULL, NULL, false, NULL, 'equation', 'full', 'Math environment: appears inside an equation block; KaTeX renders the matrix / cases / array structure.', 'math-env', NULL),
  ('arraybackslash', 'command', NULL, NULL, false, 'layout', NULL, 'partial', 'Structural / layout directive: recognised by parser regex; surrounding content preserved but fine-grained behaviour (numbering, class-aware styling) is not replicated in the Lilia block model.', 'parser-regex', NULL),
  ('ast', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('beta', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('bibitem', 'command', NULL, NULL, false, NULL, NULL, 'full', 'Parser regex preserves and routes: bibitem inside thebibliography env, bibliographystyle strip-and-record, toprule/midrule/bottomrule inline booktabs rules kept inside table output, par honoured as paragraph break.', 'parser-regex', '\bibitem{|CURSOR|}'),
  ('bibliographystyle', 'command', NULL, NULL, false, NULL, NULL, 'full', 'Parser regex preserves and routes: bibitem inside thebibliography env, bibliographystyle strip-and-record, toprule/midrule/bottomrule inline booktabs rules kept inside table output, par honoured as paragraph break.', 'parser-regex', '\bibliographystyle{|CURSOR|}'),
  ('bigcap', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('bigcup', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('bottomrule', 'command', NULL, NULL, false, NULL, NULL, 'full', 'Parser regex preserves and routes: bibitem inside thebibliography env, bibliographystyle strip-and-record, toprule/midrule/bottomrule inline booktabs rules kept inside table output, par honoured as paragraph break.', 'parser-regex', '\bottomrule'),
  ('bullet', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('cap', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('cdot', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('centering', 'command', NULL, NULL, false, 'layout', NULL, 'shimmed', 'Center-align the following text or selection', 'parser-regex', '\centering '),
  ('chi', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('cong', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('cup', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('ddots', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('DeclareMathOperator', 'command', NULL, NULL, false, 'macro', NULL, 'partial', 'Macro-definition command: first argument (the new name) survives via catch-all; the definition body is not re-executed. Usages of the defined macro still work via their own handlers when the name is already known.', 'inline-catch-all', '\DeclareMathOperator{\|CURSOR|}{name}'),
  ('delta', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('Delta', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('div', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('documentclass', 'command', NULL, NULL, false, NULL, NULL, 'full', NULL, 'parser-regex', '\documentclass{|CURSOR|}'),
  ('dot', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('draw', 'command', NULL, NULL, false, 'graphics', NULL, 'partial', 'TikZ / pgfplots internal; preserved as part of the enclosing tikzpicture / axis env which is already a passthrough block.', 'passthrough', NULL),
  ('emptyset', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('EndFor', 'command', NULL, NULL, false, NULL, 'algorithm', 'full', 'Parsed by LatexParser.ParseAlgorithmicLines into a typed ImportAlgorithmLine inside the enclosing algorithm block. Case-insensitive match against the algorithmic command regex.', 'algorithmic', NULL),
  ('ensuremath', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('epsilon', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('equiv', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('eta', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('exists', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('fill', 'command', NULL, NULL, false, 'graphics', NULL, 'partial', 'TikZ / pgfplots internal; preserved as part of the enclosing tikzpicture / axis env which is already a passthrough block.', 'passthrough', NULL),
  ('For', 'command', NULL, NULL, false, NULL, 'algorithm', 'full', 'Parsed by LatexParser.ParseAlgorithmicLines into a typed ImportAlgorithmLine inside the enclosing algorithm block. Case-insensitive match against the algorithmic command regex.', 'algorithmic', NULL),
  ('forall', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'algorithmic', NULL),
  ('frac', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('frontmatter', 'command', NULL, NULL, false, 'layout', NULL, 'partial', 'Structural / layout directive: recognised by parser regex; surrounding content preserved but fine-grained behaviour (numbering, class-aware styling) is not replicated in the Lilia block model.', 'parser-regex', '\frontmatter'),
  ('gamma', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('Gamma', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('ge', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('gets', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('hat', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('Huge', 'command', NULL, NULL, false, 'font', NULL, 'shimmed', 'Auto-inserted by parser — not yet cataloged. Review and upgrade coverage_level when implementing handling.', 'shim', '{\Huge |SELECTION|}'),
  ('in', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('infty', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('int', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('iota', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('kappa', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('lambda', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('Lambda', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('ldots', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('left', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('leftarrow', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('Leftarrow', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('leftrightarrow', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('Leftrightarrow', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('lim', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('linewidth', 'command', NULL, NULL, false, NULL, NULL, 'full', 'Length-unit token consumed by \includegraphics[width=...] argument parsing. No standalone handler; not meaningful outside width/height contexts.', 'parser-regex', '\linewidth'),
  ('mainmatter', 'command', NULL, NULL, false, 'layout', NULL, 'partial', 'Structural / layout directive: recognised by parser regex; surrounding content preserved but fine-grained behaviour (numbering, class-aware styling) is not replicated in the Lilia block model.', 'parser-regex', '\mainmatter'),
  ('mathbb', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('mathbf', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('mathcal', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('mathrm', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('midrule', 'command', NULL, NULL, false, NULL, NULL, 'full', 'Parser regex preserves and routes: bibitem inside thebibliography env, bibliographystyle strip-and-record, toprule/midrule/bottomrule inline booktabs rules kept inside table output, par honoured as paragraph break.', 'parser-regex', '\midrule'),
  ('mp', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('mu', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('multicolumn', 'command', NULL, NULL, false, 'table', NULL, 'partial', 'Multi-cell directive: cell text preserved via catch-all; spanning geometry may not match exactly on the Lilia table block.', 'inline-catch-all', '\multicolumn{|CURSOR|}{c}{|SELECTION|}'),
  ('multirow', 'command', NULL, NULL, false, 'table', NULL, 'partial', 'Multi-cell directive: cell text preserved via catch-all; spanning geometry may not match exactly on the Lilia table block.', 'inline-catch-all', '\multirow{|CURSOR|}{*}{|SELECTION|}'),
  ('nabla', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('newcommand', 'command', NULL, NULL, false, 'macro', NULL, 'partial', 'Macro-definition command: first argument (the new name) survives via catch-all; the definition body is not re-executed. Usages of the defined macro still work via their own handlers when the name is already known.', 'inline-catch-all', '\newcommand{\|CURSOR|}{}'),
  ('newcounter', 'command', NULL, NULL, false, 'macro', NULL, 'partial', 'Macro-definition command: first argument (the new name) survives via catch-all; the definition body is not re-executed. Usages of the defined macro still work via their own handlers when the name is already known.', 'inline-catch-all', '\newcounter{|CURSOR|}'),
  ('newenvironment', 'command', NULL, NULL, false, 'macro', NULL, 'partial', 'Macro-definition command: first argument (the new name) survives via catch-all; the definition body is not re-executed. Usages of the defined macro still work via their own handlers when the name is already known.', 'inline-catch-all', '\newenvironment{|CURSOR|}{}{}'),
  ('newtheorem', 'command', NULL, NULL, false, 'macro', NULL, 'partial', 'Macro-definition command: first argument (the new name) survives via catch-all; the definition body is not re-executed. Usages of the defined macro still work via their own handlers when the name is already known.', 'inline-catch-all', '\newtheorem{|CURSOR|}{name}'),
  ('newtheorem*', 'command', NULL, NULL, false, 'macro', NULL, 'partial', 'Macro-definition command: first argument (the new name) survives via catch-all; the definition body is not re-executed. Usages of the defined macro still work via their own handlers when the name is already known.', 'inline-catch-all', '\newtheorem*{|CURSOR|}{name}'),
  ('noindent', 'command', NULL, NULL, false, 'layout', NULL, 'shimmed', 'Suppress paragraph indent on the next line', 'parser-regex', '\noindent '),
  ('normalsize', 'command', NULL, NULL, false, 'font', NULL, 'shimmed', 'Auto-inserted by parser — not yet cataloged. Review and upgrade coverage_level when implementing handling.', 'shim', '{\normalsize |SELECTION|}'),
  ('notin', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('nu', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('oint', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('omega', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('Omega', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('onslide', 'command', NULL, NULL, false, 'layout', NULL, 'partial', 'Beamer overlay specifier: overlay timing stripped at import; slide content preserved.', 'inline-catch-all', NULL),
  ('overbrace', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('par', 'command', NULL, NULL, false, NULL, NULL, 'full', 'Parser regex preserves and routes: bibitem inside thebibliography env, bibliographystyle strip-and-record, toprule/midrule/bottomrule inline booktabs rules kept inside table output, par honoured as paragraph break.', 'parser-regex', '\par '),
  ('parbox', 'command', NULL, NULL, false, 'layout', NULL, 'partial', 'Box command: content preserved via catch-all; explicit dimensions / float behaviour not rendered.', 'inline-catch-all', NULL),
  ('partial', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('pause', 'command', NULL, NULL, false, 'layout', NULL, 'partial', 'Beamer overlay specifier: overlay timing stripped at import; slide content preserved.', 'inline-catch-all', NULL),
  ('phi', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('Phi', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('pi', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('Pi', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('pm', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('prod', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('propto', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('providecommand', 'command', NULL, NULL, false, 'macro', NULL, 'partial', 'Macro-definition command: first argument (the new name) survives via catch-all; the definition body is not re-executed. Usages of the defined macro still work via their own handlers when the name is already known.', 'inline-catch-all', NULL),
  ('psi', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('Psi', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('quad', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('raggedleft', 'command', NULL, NULL, false, 'layout', NULL, 'shimmed', 'Right-align text', 'parser-regex', '\raggedleft '),
  ('raggedright', 'command', NULL, NULL, false, 'layout', NULL, 'shimmed', 'Right-align disabled / left-justified text', 'parser-regex', '\raggedright '),
  ('renewcommand', 'command', NULL, NULL, false, 'macro', NULL, 'partial', 'Macro-definition command: first argument (the new name) survives via catch-all; the definition body is not re-executed. Usages of the defined macro still work via their own handlers when the name is already known.', 'inline-catch-all', NULL),
  ('rho', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('right', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('rightarrow', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('Rightarrow', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('section*', 'command', NULL, NULL, false, 'heading', NULL, 'partial', 'Starred section variant: parser section regex does not match the *-suffix. Title text survives via catch-all; numbering / hierarchy not preserved.', 'inline-catch-all', NULL),
  ('setcounter', 'command', NULL, NULL, false, 'layout', NULL, 'partial', 'Structural / layout directive: recognised by parser regex; surrounding content preserved but fine-grained behaviour (numbering, class-aware styling) is not replicated in the Lilia block model.', 'parser-regex', NULL),
  ('setlength', 'command', NULL, NULL, false, 'layout', NULL, 'partial', 'Structural / layout directive: recognised by parser regex; surrounding content preserved but fine-grained behaviour (numbering, class-aware styling) is not replicated in the Lilia block model.', 'parser-regex', NULL),
  ('setminus', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('sigma', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('Sigma', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('sim', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('sqrt', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('star', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('State', 'command', NULL, NULL, false, NULL, 'algorithm', 'full', 'Parsed by LatexParser.ParseAlgorithmicLines into a typed ImportAlgorithmLine inside the enclosing algorithm block. Case-insensitive match against the algorithmic command regex.', 'algorithmic', NULL),
  ('subequations', 'environment', NULL, NULL, false, NULL, 'equation', 'full', 'Math environment: appears inside an equation block; KaTeX renders the matrix / cases / array structure.', 'math-env', NULL),
  ('subset', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('subseteq', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('sum', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('tau', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('text', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('textsuperscript', 'command', NULL, NULL, false, 'font', NULL, 'partial', 'Font variant: content preserved via inline catch-all; font family / weight / shape not rendered.', 'inline-catch-all', NULL),
  ('textwidth', 'command', NULL, NULL, false, 'layout', NULL, 'partial', 'Structural / layout directive: recognised by parser regex; surrounding content preserved but fine-grained behaviour (numbering, class-aware styling) is not replicated in the Lilia block model.', 'parser-regex', NULL),
  ('thanks', 'command', NULL, NULL, false, NULL, NULL, 'full', 'Stripped from body by LatexParser.StripBalancedCommand; argument contributes to document metadata (author affiliation / funding acknowledgement).', 'metadata-extract', '\thanks{|SELECTION|}'),
  ('thepage', 'command', NULL, NULL, false, 'layout', NULL, 'partial', 'Structural / layout directive: recognised by parser regex; surrounding content preserved but fine-grained behaviour (numbering, class-aware styling) is not replicated in the Lilia block model.', 'parser-regex', NULL),
  ('theta', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('Theta', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('tilde', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('times', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('to', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('toprule', 'command', NULL, NULL, false, NULL, NULL, 'full', 'Parser regex preserves and routes: bibitem inside thebibliography env, bibliographystyle strip-and-record, toprule/midrule/bottomrule inline booktabs rules kept inside table output, par honoured as paragraph break.', 'parser-regex', '\toprule'),
  ('underbrace', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('upsilon', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('Upsilon', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('usepackage', 'command', NULL, NULL, false, NULL, NULL, 'full', NULL, 'parser-regex', NULL),
  ('vdots', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('xi', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('Xi', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL),
  ('zeta', 'command', NULL, NULL, false, NULL, 'equation', 'full', 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.', 'math-katex', NULL);

-- A row the parser inserted after the migrations ran: still exactly its placeholder.
UPDATE latex_tokens t
   SET arity = b.arity, optional_arity = b.optional_arity, expects_body = b.expects_body,
       semantic_category = b.semantic_category, maps_to_block_type = b.maps_to_block_type,
       coverage_level = b.coverage_level, notes = b.notes, handler_kind = b.handler_kind,
       insert_template = b.insert_template, updated_at = NOW()
  FROM catalog_backfill b
 WHERE t.name = b.name AND t.kind = b.kind AND t.package_slug IS NULL
   AND t.coverage_level = 'unsupported'
   AND t.notes LIKE 'Auto-inserted by parser%'
   AND t.handler_kind IS NULL AND t.insert_template IS NULL;

-- Six commands later promoted to 'shimmed' that no migration gave a handler, here or on a
-- database migrated in order: the size commands the parser strips with their siblings (shim, as
-- 20260620141404 gave \huge … \tiny), and the layout commands its regexes consume.
UPDATE latex_tokens SET handler_kind = 'shim', updated_at = NOW()
 WHERE kind = 'command' AND package_slug IS NULL AND handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed') AND name IN ('Huge','normalsize');
UPDATE latex_tokens SET handler_kind = 'parser-regex', updated_at = NOW()
 WHERE kind = 'command' AND package_slug IS NULL AND handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed') AND name IN ('centering','raggedright','raggedleft','noindent');

-- No row yet: make it now, so the parser finds it instead of inserting a placeholder.
INSERT INTO latex_tokens (id, name, kind, package_slug, arity, optional_arity, expects_body,
                          semantic_category, maps_to_block_type, coverage_level, notes,
                          handler_kind, insert_template, created_at, updated_at)
SELECT gen_random_uuid(), b.name, b.kind, NULL, b.arity, b.optional_arity, b.expects_body,
       b.semantic_category, b.maps_to_block_type, b.coverage_level, b.notes,
       b.handler_kind, b.insert_template, NOW(), NOW()
  FROM catalog_backfill b
 WHERE NOT EXISTS (SELECT 1 FROM latex_tokens t
                    WHERE t.name = b.name AND t.kind = b.kind AND t.package_slug IS NULL);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op, like UpgradeKernelTokenCoverage: turning these back into 'unsupported'
            // would only restore the bug.
        }
    }
}
