using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Coverage honesty pass 3 — math commands + environments.
    ///
    /// These 186 math commands + 8 math environments + 6 theorem-like
    /// environments render correctly via KaTeX inside equation blocks
    /// (and as inline math between $...$). The parser doesn't have
    /// dedicated handlers for each symbol; instead it preserves math
    /// segments verbatim and KaTeX handles rendering downstream.
    ///
    /// That's "full" from a user standpoint: the published coverage tab
    /// should reflect that documents using Greek letters, math operators,
    /// matrix envs, and standard theorem envs render as expected.
    ///
    /// Handler story, for the parser-reads-catalog target (see
    /// lilia-docs/technical/latex-coverage-architecture.md):
    ///
    ///   handler_kind 'math-katex'  — preserved by the parser's math-
    ///                                segment extractor; KaTeX renders
    ///                                inside equation / inline blocks.
    ///   handler_kind 'math-env'    — matched as a math environment
    ///                                nested inside equation-like block;
    ///                                KaTeX-renderable structure.
    ///   handler_kind 'theorem-like' — matched by TheoremEnvironments
    ///                                dict in LatexParser.
    ///
    /// Idempotent — each UPDATE guarded by coverage_level = 'unsupported'.
    /// </summary>
    public partial class CoverageHonestyPass3MathPromotion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Math commands (186) — Greek letters, operators, arrows,
            // relations, set theory, logic, math fonts, accents, dots,
            // delimiters, fractions, binomials, utilities.
            migrationBuilder.Sql(@"
UPDATE latex_tokens
   SET coverage_level = 'full',
       maps_to_block_type = 'equation',
       notes = 'Math command: preserved verbatim by the parser math-segment extractor and rendered by KaTeX inside equation / inline-math contexts.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'command'
   AND name IN (
     -- Greek (upper + lower case)
     'alpha','beta','gamma','delta','epsilon','zeta','eta','theta','iota','kappa',
     'lambda','mu','nu','xi','pi','rho','sigma','tau','upsilon','phi','chi','psi','omega',
     'Gamma','Delta','Theta','Lambda','Xi','Pi','Sigma','Upsilon','Phi','Psi','Omega',
     -- Sums / products / integrals / bigops
     'sum','prod','int','iint','iiint','oint','bigcup','bigcap','bigoplus','bigotimes',
     -- Limits / sup / inf
     'lim','liminf','limsup','max','min','sup','inf',
     -- Function names
     'log','ln','sin','cos','tan','sec','csc','cot','arcsin','arccos','arctan',
     'sinh','cosh','tanh','exp','det','dim','ker','deg','Pr','gcd',
     -- Relations
     'le','ge','ne','equiv','sim','simeq','approx','cong','propto',
     'prec','succ','preceq','succeq','ll','gg',
     -- Set theory
     'in','notin','subset','subseteq','supset','cup','cap','setminus',
     'emptyset','varnothing',
     -- Arrows
     'to','rightarrow','leftarrow','leftrightarrow','Rightarrow','Leftarrow','Leftrightarrow',
     'mapsto','hookrightarrow','twoheadrightarrow','implies','iff','uparrow','downarrow','gets',
     -- Logic
     'forall','exists','nexists','neg','land','lor','top','bot','vdash','models',
     -- Binary ops / symbols
     'cdot','times','div','pm','mp','ast','star','circ','bullet',
     'oplus','otimes','ominus','odot',
     -- Constants / misc symbols
     'infty','partial','nabla','prime','aleph','hbar','degree','angle',
     -- Fractions / roots / binomials
     'frac','dfrac','tfrac','binom','dbinom','sqrt',
     -- Dots
     'dots','ldots','cdots','vdots','ddots',
     -- Math fonts
     'mathbb','mathbf','mathcal','mathrm','mathit','mathfrak','mathscr','mathsf',
     -- Delimiters
     'left','right','langle','rangle','lceil','rceil','lfloor','rfloor',
     -- Accents
     'hat','tilde','bar','vec','dot','ddot','breve','check',
     'overline','overbrace','underbrace','widehat','widetilde',
     -- Utilities
     'ensuremath','notag','text','quad','qquad'
   );
");

            // Math environments (8) — cases / matrix family / array /
            // subequations. All render inside equation blocks via KaTeX.
            migrationBuilder.Sql(@"
UPDATE latex_tokens
   SET coverage_level = 'full',
       maps_to_block_type = 'equation',
       notes = 'Math environment: appears inside an equation block; KaTeX renders the matrix / cases / array structure.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'environment'
   AND name IN ('cases','pmatrix','bmatrix','vmatrix','Vmatrix','smallmatrix','array','subequations');
");

            // Theorem-like envs (6) — kernel-scope dupes auto-inserted
            // by the parser's catalog scanner. Originals already at
            // 'full' under amsthm scope. LatexParser.TheoremEnvironments
            // matches them case-insensitively and maps to ImportTheorem
            // with a typed TheoremEnvironmentType.
            migrationBuilder.Sql(@"
UPDATE latex_tokens
   SET coverage_level = 'full',
       maps_to_block_type = 'theorem',
       notes = 'Theorem-like env: matched by LatexParser.TheoremEnvironments, imported as ImportTheorem with a typed TheoremEnvironmentType. Duplicate of the amsthm-scoped catalog row.',
       updated_at = NOW()
 WHERE coverage_level = 'unsupported'
   AND kind = 'environment'
   AND name IN ('corollary','definition','lemma','proof','proposition','thm');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op — these rows have validated KaTeX / theorem-env
            // handling. Demoting is exactly what this migration fixes.
        }
    }
}
