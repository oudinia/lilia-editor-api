using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Stage 2 of the parser-reads-catalog plan
    /// (lilia-docs/technical/latex-coverage-architecture.md).
    ///
    /// Adds <c>handler_kind</c> to <c>latex_tokens</c> and backfills it
    /// for every row that currently claims any non-'unsupported'
    /// coverage_level. The column becomes the contract between the
    /// catalog and LatexParser.cs — CI asserts every full / partial /
    /// shimmed row has a handler_kind from a whitelisted set.
    ///
    /// Backfill follows the 2026-04-22 audit categorization, most
    /// specific first:
    ///
    ///   1. shim                 — class-aware rewrite before dispatch
    ///   2. algorithmic          — ParseAlgorithmicLines typed-line regex
    ///   3. section-regex        — \section .. \subparagraph extractor
    ///   4. citation-regex       — cite / citep / citet / … pattern
    ///   5. metadata-extract     — title / author / date / caption etc.
    ///   6. inline-preserved     — PreservedInlineCommands set
    ///   7. inline-code          — CodeDisplayInlineCommands set
    ///   8. inline-markdown      — MarkdownInlineWrappers (textbf / textit / …)
    ///   9. theorem-like         — TheoremEnvironments dict (env kind)
    ///  10. known-structural     — KnownEnvironments set (env kind)
    ///  11. pass-through         — PassThroughEnvironments set (env kind)
    ///  12. math-env             — cases / matrix family / array / subequations
    ///  13. math-katex           — Greek / operators / symbols / fractions / …
    ///  14. parser-regex         — loose specific-regex handling
    ///
    /// Anything claiming full / partial / shimmed that isn't caught by
    /// 1-14 gets 'UNCLASSIFIED' so the CI test fails loudly, forcing a
    /// follow-up (demote row or add handler).
    ///
    /// Idempotent: UPDATEs are guarded by handler_kind IS NULL AND
    /// coverage_level IN ('full','partial','shimmed').
    /// </summary>
    public partial class AddTokenHandlerKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "handler_kind",
                table: "latex_tokens",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.Sql(@"
-- 1. Shim paths (class-aware preamble rewrites, commit 9afe55c)
UPDATE latex_tokens SET handler_kind = 'shim'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'environment'
   AND name IN ('tabularx','rSection');

UPDATE latex_tokens SET handler_kind = 'shim'
 WHERE handler_kind IS NULL
   AND coverage_level = 'shimmed';

-- 2. Algorithmic regex (ParseAlgorithmicLines)
UPDATE latex_tokens SET handler_kind = 'algorithmic'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'command'
   AND lower(name) IN (
     'require','ensure','state','return','print','comment',
     'if','elsif','elseif','else','endif',
     'for','forall','endfor',
     'while','endwhile',
     'repeat','until','loop','endloop'
   );

-- 3. Section regex
UPDATE latex_tokens SET handler_kind = 'section-regex'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'command'
   AND name IN ('section','subsection','subsubsection','paragraph','subparagraph');

-- 4. Citation regex (note: some names overlap with inline-preserved;
-- we pick citation-regex here as the more specific handler)
UPDATE latex_tokens SET handler_kind = 'citation-regex'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'command'
   AND name IN (
     'cite','citep','citet','citealp','citealt',
     'parencite','textcite','footcite','autocite','nocite'
   );

-- 5. Metadata-extract (MatchBalanced + StripBalancedCommand)
UPDATE latex_tokens SET handler_kind = 'metadata-extract'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'command'
   AND name IN ('title','author','date','caption','thanks','affil','affiliation');

-- 6. Inline-preserved (kept verbatim by NormaliseInlineCommands)
UPDATE latex_tokens SET handler_kind = 'inline-preserved'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'command'
   AND name IN (
     'citeauthor','citeyear',
     'ref','pageref','eqref','autoref','cref','Cref',
     'label','href','url','hyperref',
     'footnote','footnotemark','footnotetext',
     'input','include',
     'printbibliography'
   );

-- 7. Inline-code (backtick-wrapping)
UPDATE latex_tokens SET handler_kind = 'inline-code'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'command'
   AND name IN (
     'texttt','inlinecode','code','cmdname','macroname','pkgname',
     'filename','path','envname','lstinline','mintinline','verb'
   );

-- 8. Inline-markdown (textbf/textit/emph/underline → MD wrappers)
UPDATE latex_tokens SET handler_kind = 'inline-markdown'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'command'
   AND name IN ('textbf','textit','emph','underline');

-- 9. Theorem-like environments
UPDATE latex_tokens SET handler_kind = 'theorem-like'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'environment'
   AND lower(name) IN (
     'theorem','thm','lemma','proposition','prop','corollary','cor',
     'conjecture','definition','defn','example','remark','note',
     'proof','exercise','solution','axiom'
   );

-- 10. Known-structural environments (KnownEnvironments set)
UPDATE latex_tokens SET handler_kind = 'known-structural'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'environment'
   AND lower(name) IN (
     'document','abstract','thebibliography',
     'equation','align','gather','multline','eqnarray',
     'lstlisting','verbatim','minted',
     'figure','subfigure','table','tabular',
     'itemize','enumerate','description',
     'quote','quotation','verse',
     'center','flushleft','flushright',
     'algorithm','algorithm2e','algorithmic'
   );

-- 11. Pass-through environments (wrapper dropped, body parsed)
UPDATE latex_tokens SET handler_kind = 'pass-through'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'environment'
   AND name IN (
     'spacing','singlespace','doublespace','onehalfspace',
     'raggedright','raggedleft',
     'small','footnotesize','scriptsize','tiny',
     'large','Large','LARGE','huge','Huge','normalsize'
   );

-- 12. Math environments (KaTeX inside equation block)
UPDATE latex_tokens SET handler_kind = 'math-env'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'environment'
   AND name IN (
     'cases','pmatrix','bmatrix','vmatrix','Vmatrix','smallmatrix',
     'array','subequations',
     -- starred / less-common math envs in the catalog
     'align*','gather*','matrix'
   );

-- 13. Math commands (KaTeX renders inside math mode)
UPDATE latex_tokens SET handler_kind = 'math-katex'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'command'
   AND name IN (
     'alpha','beta','gamma','delta','epsilon','zeta','eta','theta','iota','kappa',
     'lambda','mu','nu','xi','pi','rho','sigma','tau','upsilon','phi','chi','psi','omega',
     'Gamma','Delta','Theta','Lambda','Xi','Pi','Sigma','Upsilon','Phi','Psi','Omega',
     'sum','prod','int','iint','iiint','oint','bigcup','bigcap','bigoplus','bigotimes',
     'lim','liminf','limsup','max','min','sup','inf',
     'log','ln','sin','cos','tan','sec','csc','cot','arcsin','arccos','arctan',
     'sinh','cosh','tanh','exp','det','dim','ker','deg','Pr','gcd',
     'le','ge','ne','equiv','sim','simeq','approx','cong','propto',
     'prec','succ','preceq','succeq','ll','gg',
     'in','notin','subset','subseteq','supset','cup','cap','setminus',
     'emptyset','varnothing',
     'to','rightarrow','leftarrow','leftrightarrow',
     'Rightarrow','Leftarrow','Leftrightarrow',
     'mapsto','hookrightarrow','twoheadrightarrow','implies','iff',
     'uparrow','downarrow','gets',
     'forall','exists','nexists','neg','land','lor','top','bot','vdash','models',
     'cdot','times','div','pm','mp','ast','star','circ','bullet',
     'oplus','otimes','ominus','odot',
     'infty','partial','nabla','prime','aleph','hbar','degree','angle',
     'frac','dfrac','tfrac','binom','dbinom','sqrt',
     'dots','ldots','cdots','vdots','ddots',
     'mathbb','mathbf','mathcal','mathrm','mathit','mathfrak','mathscr','mathsf',
     'left','right','langle','rangle','lceil','rceil','lfloor','rfloor',
     'hat','tilde','bar','vec','dot','ddot','breve','check',
     'overline','overbrace','underbrace','widehat','widetilde',
     'ensuremath','notag','text','quad','qquad','eqref'
   );

-- 14. Parser-regex (specific regex in LatexParser.cs but not fitting
-- any category above — loose handling like heading-level preservation,
-- bibitem inside bibliography env, booktabs rules kept inside table
-- output, paragraph breaks, document-class / package declarations,
-- page / line / TOC commands, beamer frame decorations, etc.)
UPDATE latex_tokens SET handler_kind = 'parser-regex'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'command'
   AND name IN (
     'documentclass','usepackage',
     'bibitem','bibliography','bibliographystyle',
     'toprule','midrule','bottomrule','hline','cline',
     'par','newpage','clearpage','pagebreak','pagestyle','geometry',
     'includegraphics','titlepage','maketitle','tableofcontents',
     'framesubtitle','frametitle','part','chapter',
     'bigskip','medskip','smallskip',
     'textrm','textsf','textsl','textsc','textmd','textnormal','textup',
     'textsuperscript','textsubscript',
     'hfill','hspace','vspace','linebreak',
     'linewidth',
     'name','phone','email','photo','homepage','social','extrainfo','address',
     'cventry','cvitem',
     'titleformat','titlespacing','setdefaultlanguage','setstretch',
     'doublespacing','singlespacing','onehalfspacing',
     'headrulewidth','footrulewidth',
     'LaTeX','TeX'
   );

-- 14b. parser-regex for environments in catalog metadata not covered
-- by the sets above (letter, minipage, wrapfigure, multicols, etc.).
UPDATE latex_tokens SET handler_kind = 'passthrough'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'environment';

-- 14c. remaining command rows fall into inline-catch-all (arg extracted,
-- command name dropped)
UPDATE latex_tokens SET handler_kind = 'inline-catch-all'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND kind = 'command';

-- 15. Anything still NULL at full/partial/shimmed is a gap — mark so
-- the CI test can flag it. (Belt and braces: step 14a/b/c should cover
-- everything remaining, but we keep this for future rows that don't
-- fit the patterns above.)
UPDATE latex_tokens SET handler_kind = 'UNCLASSIFIED'
 WHERE handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "handler_kind",
                table: "latex_tokens");
        }
    }
}
