using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
namespace Lilia.Engines;

/// <summary>
/// Single source of truth for the LaTeX preamble used across
/// full document export, per-block validation, and rendering.
///
/// <para><b>How <c>bm</c> got here, and why <c>physics</c> did not.</b> Six
/// candidate packages were ranked by what they actually recover: each was added
/// to the preamble and the 65 commands nothing could compile were re-probed,
/// scored by how many TeX.SE posts use them. Then — the part that decided it —
/// each candidate was checked for what it <i>breaks</i>, by recompiling all 374
/// corpus samples and diffing the pass set against baseline.</para>
///
/// <code>
///            recovers   reach    newly broken
/// bm             1      30 posts       0
/// physics        3      23 posts       2   ← \qty, \meter
/// </code>
///
/// <para><c>physics</c> loads perfectly well, which is why "does it load" is
/// too weak a question to decide on. It redefines <c>\qty</c>, colliding with
/// <c>siunitx</c> above, so it trades two recovered samples for two broken ones
/// and damages unit-heavy documents to do it.</para>
///
/// <para>The measured basis is <c>latex_facts.command_support</c>; the write-up
/// is <c>lilia-docs/plan/command-support-dataset.md</c>.</para>
/// </summary>
public static partial class LaTeXPreamble
{
    /// <summary>
    /// Every package name the preamble actually loads, read out of the preamble
    /// itself.
    /// </summary>
    /// <remarks>
    /// <para><b>Derived rather than maintained.</b> Adding a package used to
    /// mean editing three places by hand — <see cref="Packages"/>,
    /// <see cref="ValidationPackages"/>, and the skip-list in
    /// <c>LaTeXExportService</c> that stops an imported document loading the
    /// same package twice. Nothing enforced agreement between them, and the two
    /// ways of getting it wrong both hide:</para>
    ///
    /// <list type="bullet">
    /// <item>miss validation, and a block validates differently from how it
    /// exports — worst when it validates as broken and exports fine, which
    /// tells an author their document is wrong when it is not;</item>
    /// <item>miss the skip-list, and any document importing that package aborts
    /// with "Option clash for package" — a failure that only appears on
    /// documents which happen to import it, so it survives local testing
    /// entirely.</item>
    /// </list>
    ///
    /// <para>Reading the list out of the preamble makes the first impossible and
    /// removes the hand-maintained half of the second.</para>
    ///
    /// <para><b>What this deliberately does not cover.</b> The skip-list also
    /// contains packages we do <i>not</i> load and must still refuse — typeface
    /// swappers that redefine commands amsmath owns, engine-only font loaders,
    /// and packages we shim. Those cannot be derived from a preamble that never
    /// mentions them, so they stay written down where the reason can be
    /// written down with them.</para>
    /// </remarks>
    public static IReadOnlySet<string> LoadedPackageNames => LazyLoadedPackages.Value;

    private static readonly Lazy<IReadOnlySet<string>> LazyLoadedPackages = new(() =>
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Both variants, because a package loaded for validation but not export
        // (or the reverse) is exactly the drift this is meant to prevent.
        foreach (var source in new[] { Packages, ValidationPackages })
        {
            CollectPackageNames(source, names);
        }

        // fontspec arrives only for the Unicode engines, so it is not in either
        // constant above — but an imported \usepackage{fontspec} must still be
        // skipped when we are the ones loading it.
        foreach (var engine in new[] { LatexEngine.Xelatex, LatexEngine.Lualatex })
        {
            CollectPackageNames(EngineAddendum(engine), names);
        }

        return names;
    });

    private static void CollectPackageNames(string preamble, HashSet<string> into)
    {
        foreach (var line in preamble.Split('\n'))
        {
            // A commented-out \usepackage is not loaded, and treating it as
            // loaded would silently drop it from an imported document.
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('%')) continue;

            foreach (Match match in UsePackageLine().Matches(trimmed))
            {
                // One \usepackage can name several: amsmath,amssymb,amsfonts.
                foreach (var name in match.Groups["names"].Value.Split(','))
                {
                    var cleaned = name.Trim();
                    if (cleaned.Length > 0) into.Add(cleaned);
                }
            }
        }
    }

    /// <summary>
    /// <c>\usepackage</c> with optional bracketed options. The options group is
    /// non-greedy and excludes <c>]</c>, so
    /// <c>\usepackage[dvipsnames,svgnames,table]{xcolor}</c> yields
    /// <c>xcolor</c> rather than swallowing the options as a name.
    /// </summary>
    [GeneratedRegex(@"\\usepackage\s*(?:\[[^\]]*\])?\s*\{(?<names>[^}]*)\}")]
    private static partial Regex UsePackageLine();

    /// <summary>
    /// Engine-specific preamble addendum. Pdflatex gets nothing
    /// (Packages already loads inputenc/fontenc/lmodern). Lualatex
    /// and xelatex get `\usepackage{fontspec}` so blocks containing
    /// `\setmainfont{...}` (the TUG font-catalogue hook) compile
    /// instead of failing with "Undefined control sequence".
    ///
    /// fontspec automatically disables the Type-1 font setup that
    /// inputenc/fontenc/lmodern pull in — both load orders are
    /// safe with lua/xelatex (the package itself defends).
    /// </summary>
    public static string EngineAddendum(LatexEngine engine) => engine switch
    {
        LatexEngine.Lualatex or LatexEngine.Xelatex => @"% Engine: lua/xelatex — load fontspec so \setmainfont{…} works.
\usepackage{fontspec}
",
        _ => "",
    };

    /// <summary>
    /// All packages needed by the Lilia block system.
    /// Order matters: encoding → math → typography → graphics → tables → lists → code → special → links.
    /// hyperref must come before cleveref.
    /// </summary>
    public const string Packages = @"% Encoding & fonts
\usepackage[utf8]{inputenc}
\usepackage[T1]{fontenc}
\usepackage{textcomp}
\usepackage{lmodern}        % Latin Modern: better scaling + PDF-embedding than default Computer Modern

% Math
\usepackage{amsmath,amssymb,amsfonts,amsthm}
\usepackage{mathtools}
\usepackage{mathrsfs}
\usepackage{cancel}
\usepackage{siunitx}
% bm: \bm{v} for bold maths. Added on measurement, not taste — see the note on
% LaTeXPreamble for what was measured and what was rejected.
\usepackage{bm}

% Typography
\usepackage{microtype}
\usepackage{setspace}
\usepackage{parskip}

% Graphics & floats
\usepackage{graphicx}
\usepackage{float}
\usepackage{caption}
\usepackage{subcaption}
\usepackage[dvipsnames,svgnames,table]{xcolor}    % ~200+ named colors (red/blue/… + Apricot/ForestGreen/… + DarkOrchid/MidnightBlue/…)

% Tables
\usepackage{booktabs}
\usepackage{multirow}
\usepackage{tabularx}
\usepackage{longtable}
\usepackage{array}

% Lists
\usepackage{enumitem}

% Page-break intent (P2.2). needspace provides \Needspace, used by the
% keepWithNext block attribute to reserve room after a heading so LaTeX breaks
% BEFORE it rather than stranding it at the foot of a page.
\usepackage{needspace}

% Code
\usepackage{listings}

% Algorithms & pseudocode
% NOTE: we bundle algorithm + algorithmic (the legacy interface) only. Alternatives
% that users can load themselves in their own preamble:
%   - algpseudocode / algorithmicx (redefines \begin{algorithmic} — conflicts with ours)
%   - algorithm2e (floats compete with the algorithm package's floats — conflicts)
% Both would produce silent rendering bugs if bundled alongside our defaults.
\usepackage{algorithm}
\usepackage{algorithmic}

% Callouts & boxes
\usepackage{tcolorbox}

% NOTE: csquotes is NOT bundled. It is required by many biblatex styles but must be
% loaded AFTER babel/polyglossia so it can pick up language-specific quote styles.
% Since we don't bundle babel either, users who need csquotes must load both in
% the correct order themselves. Bundling csquotes alone would produce silent
% English-style quotes in French/German/Spanish documents.

% Legacy graphics compat — epsfig is arXiv's #4 most-used package; it wraps graphicx
\usepackage{epsfig}

% Text formatting
\usepackage{soul}
\usepackage{ulem}
\normalem
% Comment package — enables \begin{comment}...\end{comment} for the
% multi-line block-comment output from the editor's comment toggle.
% Inline comments use the TeX primitive \iffalse...\fi (no package).
\usepackage{comment}
% Epigraph package — enables \epigraph{text}{source} used by the
% blockquote block's `epigraph` variant. Loaded always so existing
% imports + the new picker both compile cleanly.
\usepackage{epigraph}

% Links & references (hyperref before cleveref).
% PassOptionsToPackage + plain usepackage avoids option-clash when an imported
% package (orcidlink, hyperxmp, etc.) pre-loaded hyperref with default options.
\PassOptionsToPackage{colorlinks=true,linkcolor=blue,citecolor=blue,urlcolor=blue}{hyperref}
\usepackage{url}
\usepackage{hyperref}
\usepackage[nameinlink]{cleveref}
" + UnicodeShims;

    /// <summary>
    /// Packages for per-block validation — uses [demo] graphicx so missing images don't fail.
    /// </summary>
    public const string ValidationPackages = @"% Encoding & fonts
\usepackage[utf8]{inputenc}
\usepackage[T1]{fontenc}
\usepackage{textcomp}
\usepackage{lmodern}        % Latin Modern: better scaling + PDF-embedding than default Computer Modern

% Math
\usepackage{amsmath,amssymb,amsfonts,amsthm}
\usepackage{mathtools}
\usepackage{mathrsfs}
\usepackage{cancel}
\usepackage{siunitx}
% bm: \bm{v} for bold maths. Added on measurement, not taste — see the note on
% LaTeXPreamble for what was measured and what was rejected.
\usepackage{bm}

% Typography
\usepackage{microtype}
\usepackage{setspace}
\usepackage{parskip}

% Graphics & floats (demo mode for validation — no real images needed)
\usepackage[demo]{graphicx}
\usepackage{float}
\usepackage{caption}
\usepackage{subcaption}
\usepackage[dvipsnames,svgnames,table]{xcolor}    % ~200+ named colors (red/blue/… + Apricot/ForestGreen/… + DarkOrchid/MidnightBlue/…)

% Tables
\usepackage{booktabs}
\usepackage{multirow}
\usepackage{tabularx}
\usepackage{longtable}
\usepackage{array}

% Lists
\usepackage{enumitem}

% Page-break intent (P2.2). needspace provides \Needspace, used by the
% keepWithNext block attribute to reserve room after a heading so LaTeX breaks
% BEFORE it rather than stranding it at the foot of a page.
\usepackage{needspace}

% Code
\usepackage{listings}

% Algorithms & pseudocode
% NOTE: we bundle algorithm + algorithmic (the legacy interface) only. Alternatives
% that users can load themselves in their own preamble:
%   - algpseudocode / algorithmicx (redefines \begin{algorithmic} — conflicts with ours)
%   - algorithm2e (floats compete with the algorithm package's floats — conflicts)
% Both would produce silent rendering bugs if bundled alongside our defaults.
\usepackage{algorithm}
\usepackage{algorithmic}

% Callouts & boxes
\usepackage{tcolorbox}

% NOTE: csquotes is NOT bundled. It is required by many biblatex styles but must be
% loaded AFTER babel/polyglossia so it can pick up language-specific quote styles.
% Since we don't bundle babel either, users who need csquotes must load both in
% the correct order themselves. Bundling csquotes alone would produce silent
% English-style quotes in French/German/Spanish documents.

% Legacy graphics compat — epsfig is arXiv's #4 most-used package; it wraps graphicx
\usepackage{epsfig}

% Text formatting
\usepackage{soul}
\usepackage{ulem}
\normalem
% Comment package — enables \begin{comment}...\end{comment} for the
% multi-line block-comment output from the editor's comment toggle.
% Inline comments use the TeX primitive \iffalse...\fi (no package).
\usepackage{comment}
% Epigraph package — enables \epigraph{text}{source} used by the
% blockquote block's `epigraph` variant. Loaded always so existing
% imports + the new picker both compile cleanly.
\usepackage{epigraph}

% Links & references
\PassOptionsToPackage{colorlinks=true}{hyperref}
\usepackage{url}
\usepackage{hyperref}
\usepackage[nameinlink]{cleveref}
" + UnicodeShims;

    /// <summary>
    /// Maps common literal Unicode that AI-drafted / pasted prose puts directly
    /// in text (Greek letters, sub/superscripts, math operators) onto LaTeX so
    /// pdflatex doesn't fail "Unicode character γ (U+03B3) not set up for use
    /// with LaTeX". Appended to both <see cref="Packages"/> and
    /// <see cref="ValidationPackages"/>; \newunicodechar overrides are safe even
    /// where inputenc/textcomp already cover a glyph, and harmless under
    /// lua/xelatex (which handle these natively).
    /// </summary>
    public const string UnicodeShims = @"
% [lilia] Literal-Unicode → LaTeX shims (Greek, scripts, common operators)
\usepackage{newunicodechar}
% Greek lowercase
\newunicodechar{α}{\ensuremath{\alpha}}
\newunicodechar{β}{\ensuremath{\beta}}
\newunicodechar{γ}{\ensuremath{\gamma}}
\newunicodechar{δ}{\ensuremath{\delta}}
\newunicodechar{ε}{\ensuremath{\epsilon}}
\newunicodechar{ϵ}{\ensuremath{\epsilon}}
\newunicodechar{ζ}{\ensuremath{\zeta}}
\newunicodechar{η}{\ensuremath{\eta}}
\newunicodechar{θ}{\ensuremath{\theta}}
\newunicodechar{ϑ}{\ensuremath{\vartheta}}
\newunicodechar{ι}{\ensuremath{\iota}}
\newunicodechar{κ}{\ensuremath{\kappa}}
\newunicodechar{λ}{\ensuremath{\lambda}}
\newunicodechar{μ}{\ensuremath{\mu}}
\newunicodechar{ν}{\ensuremath{\nu}}
\newunicodechar{ξ}{\ensuremath{\xi}}
\newunicodechar{π}{\ensuremath{\pi}}
\newunicodechar{ϖ}{\ensuremath{\varpi}}
\newunicodechar{ρ}{\ensuremath{\rho}}
\newunicodechar{σ}{\ensuremath{\sigma}}
\newunicodechar{ς}{\ensuremath{\varsigma}}
\newunicodechar{τ}{\ensuremath{\tau}}
\newunicodechar{υ}{\ensuremath{\upsilon}}
\newunicodechar{φ}{\ensuremath{\phi}}
\newunicodechar{ϕ}{\ensuremath{\varphi}}
\newunicodechar{χ}{\ensuremath{\chi}}
\newunicodechar{ψ}{\ensuremath{\psi}}
\newunicodechar{ω}{\ensuremath{\omega}}
% Greek uppercase (distinct LaTeX commands only)
\newunicodechar{Γ}{\ensuremath{\Gamma}}
\newunicodechar{Δ}{\ensuremath{\Delta}}
\newunicodechar{Θ}{\ensuremath{\Theta}}
\newunicodechar{Λ}{\ensuremath{\Lambda}}
\newunicodechar{Ξ}{\ensuremath{\Xi}}
\newunicodechar{Π}{\ensuremath{\Pi}}
\newunicodechar{Σ}{\ensuremath{\Sigma}}
\newunicodechar{Φ}{\ensuremath{\Phi}}
\newunicodechar{Ψ}{\ensuremath{\Psi}}
\newunicodechar{Ω}{\ensuremath{\Omega}}
% Superscripts
\newunicodechar{⁰}{\ensuremath{^0}}
\newunicodechar{¹}{\ensuremath{^1}}
\newunicodechar{²}{\ensuremath{^2}}
\newunicodechar{³}{\ensuremath{^3}}
\newunicodechar{⁴}{\ensuremath{^4}}
\newunicodechar{ⁿ}{\ensuremath{^n}}
\newunicodechar{⁺}{\ensuremath{^+}}
\newunicodechar{⁻}{\ensuremath{^-}}
% Subscripts
\newunicodechar{₀}{\ensuremath{_0}}
\newunicodechar{₁}{\ensuremath{_1}}
\newunicodechar{₂}{\ensuremath{_2}}
\newunicodechar{₃}{\ensuremath{_3}}
\newunicodechar{₄}{\ensuremath{_4}}
\newunicodechar{ₙ}{\ensuremath{_n}}
% Common operators / relations
\newunicodechar{×}{\ensuremath{\times}}
\newunicodechar{÷}{\ensuremath{\div}}
\newunicodechar{·}{\ensuremath{\cdot}}
\newunicodechar{−}{\ensuremath{-}}
\newunicodechar{±}{\ensuremath{\pm}}
\newunicodechar{∓}{\ensuremath{\mp}}
\newunicodechar{≤}{\ensuremath{\leq}}
\newunicodechar{≥}{\ensuremath{\geq}}
\newunicodechar{≠}{\ensuremath{\neq}}
\newunicodechar{≈}{\ensuremath{\approx}}
\newunicodechar{≡}{\ensuremath{\equiv}}
\newunicodechar{∝}{\ensuremath{\propto}}
\newunicodechar{∞}{\ensuremath{\infty}}
\newunicodechar{→}{\ensuremath{\rightarrow}}
\newunicodechar{←}{\ensuremath{\leftarrow}}
\newunicodechar{↔}{\ensuremath{\leftrightarrow}}
\newunicodechar{⇒}{\ensuremath{\Rightarrow}}
\newunicodechar{∂}{\ensuremath{\partial}}
\newunicodechar{∇}{\ensuremath{\nabla}}
\newunicodechar{∑}{\ensuremath{\sum}}
\newunicodechar{∏}{\ensuremath{\prod}}
\newunicodechar{∫}{\ensuremath{\int}}
\newunicodechar{√}{\ensuremath{\sqrt{}}}
\newunicodechar{∈}{\ensuremath{\in}}
\newunicodechar{∉}{\ensuremath{\notin}}
\newunicodechar{⊂}{\ensuremath{\subset}}
\newunicodechar{⊆}{\ensuremath{\subseteq}}
\newunicodechar{∪}{\ensuremath{\cup}}
\newunicodechar{∩}{\ensuremath{\cap}}
\newunicodechar{∀}{\ensuremath{\forall}}
\newunicodechar{∃}{\ensuremath{\exists}}
\newunicodechar{∅}{\ensuremath{\emptyset}}
";

    /// <summary>
    /// Theorem environment declarations used by theorem blocks.
    /// Includes both numbered and unnumbered (*) variants.
    /// </summary>
    public const string TheoremEnvironments = @"\newtheorem{theorem}{Theorem}
\newtheorem{lemma}{Lemma}
\newtheorem{proposition}{Proposition}
\newtheorem{corollary}{Corollary}
\newtheorem{definition}{Definition}
\newtheorem{example}{Example}
\newtheorem{remark}{Remark}
\newtheorem{claim}{Claim}
\newtheorem{assumption}{Assumption}
\newtheorem{axiom}{Axiom}
\newtheorem{conjecture}{Conjecture}
\newtheorem{hypothesis}{Hypothesis}
\newtheorem*{theorem*}{Theorem}
\newtheorem*{lemma*}{Lemma}
\newtheorem*{proposition*}{Proposition}
\newtheorem*{corollary*}{Corollary}
\newtheorem*{definition*}{Definition}
\newtheorem*{example*}{Example}
\newtheorem*{remark*}{Remark}
\newtheorem*{claim*}{Claim}
\newtheorem*{assumption*}{Assumption}
\newtheorem*{axiom*}{Axiom}
\newtheorem*{conjecture*}{Conjecture}
\newtheorem*{hypothesis*}{Hypothesis}
";

    /// <summary>
    /// The (lowercase, unstarred) theorem environments declared in
    /// <see cref="TheoremEnvironments"/>. The canonical set a block's
    /// theoremType may map onto.
    /// </summary>
    public static readonly HashSet<string> KnownTheoremEnvironments =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "theorem", "lemma", "proposition", "corollary", "definition",
            "example", "remark", "claim", "assumption", "axiom",
            "conjecture", "hypothesis",
        };

    /// <summary>
    /// Normalize a block's <c>theoremType</c> to a LaTeX environment the
    /// preamble actually defines. The editor stores capitalized labels
    /// ("Theorem"), but <c>\newtheorem</c> declares lowercase envs — so
    /// <c>\begin{Theorem}</c> / <c>\setcounter{Theorem}</c> fail compilation
    /// with "No counter 'Theorem' defined". Lowercase, keep amsthm's built-in
    /// unnumbered <c>proof</c> as-is, and fall back to the always-defined
    /// <c>theorem</c> for anything unrecognized.
    /// </summary>
    public static string NormalizeTheoremEnv(string? theoremType)
    {
        var t = (theoremType ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length == 0) return "theorem";
        if (t == "proof") return "proof";
        return KnownTheoremEnvironments.Contains(t) ? t : "theorem";
    }

    // Languages the bundled `listings` package ships with. A `language=` value
    // outside this set (e.g. "text", "json", or an exotic DOCX-import name)
    // causes pdflatex to fail "Package Listings Error: Couldn't load requested
    // language." Unknown languages render as plain code (no `language` option).
    private static readonly HashSet<string> ListingsLanguages = new(StringComparer.Ordinal)
    {
        "Ada", "Assembler", "Awk", "bash", "C", "C++", "Caml", "Clean", "Cobol",
        "Csh", "CSS", "Delphi", "Eiffel", "Erlang", "Euphoria", "Fortran",
        "GCL", "Gnuplot", "Haskell", "HTML", "IDL", "inform", "Java", "JVMIS",
        "ksh", "Lisp", "Logo", "Lua", "make", "Mathematica", "Matlab",
        "Mercury", "MetaPost", "Miranda", "Mizar", "ML", "Modula-2", "MuPAD",
        "NASTRAN", "Oberon-2", "OCL", "Octave", "Oz", "Pascal", "Perl", "PHP",
        "PL/I", "Plasm", "PostScript", "POV", "Prolog", "Promela", "PSTricks",
        "Python", "R", "Reduce", "Rexx", "RSL", "Ruby", "S", "SAS", "Scilab",
        "sh", "SHELXL", "Simula", "SQL", "tcl", "TeX", "VBScript", "Verilog",
        "VHDL", "VRML", "XML", "XSLT",
    };

    // Close-enough substitutions for languages listings doesn't ship natively.
    // Values must be a canonical listings name or "" (plain, no highlighting).
    private static readonly Dictionary<string, string> ListingsAliasMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["js"] = "JavaScript", ["javascript"] = "JavaScript",
        ["ts"] = "JavaScript", ["typescript"] = "JavaScript",
        ["sh"] = "bash", ["shell"] = "bash", ["zsh"] = "bash",
        ["py"] = "Python", ["cpp"] = "C++", ["c#"] = "Java", ["csharp"] = "Java",
        ["latex"] = "TeX", ["tex"] = "TeX", ["kotlin"] = "Java",
        ["json"] = "", ["yaml"] = "", ["yml"] = "", ["toml"] = "",
        ["go"] = "", ["golang"] = "", ["rust"] = "", ["swift"] = "",
        ["solidity"] = "", ["dart"] = "", ["zig"] = "", ["elixir"] = "",
        ["markdown"] = "", ["md"] = "", ["text"] = "", ["plaintext"] = "",
        ["plain"] = "", ["txt"] = "",
    };

    /// <summary>
    /// Returns a listings-valid language name (canonical casing), or "" when the
    /// input has no safe mapping (caller then omits the <c>language=</c> option).
    /// Single source of truth shared by the export + per-block-validation paths.
    /// </summary>
    public static string NormalizeListingsLanguage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var trimmed = raw.Trim();
        if (ListingsAliasMap.TryGetValue(trimmed, out var mapped)) return mapped;
        foreach (var known in ListingsLanguages)
            if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase)) return known;
        return "";
    }

    /// <summary>
    /// Shims for beamer presentations: \\usetheme, \\usecolortheme, \\usefonttheme
    /// wrapped in \\IfFileExists so institution-specific themes (sintef, wildcat,
    /// Ufg, DEXPI) degrade to the default theme when their .sty isn't on the
    /// TexLive install. Also safe no-ops for \\setbeamercolor / \\setbeamertemplate.
    /// </summary>
    public const string BeamerShims = @"% Shims for beamer theme loads that may not be on the server
\makeatletter
\providecommand{\liliaSafeUseTheme}[1]{\IfFileExists{beamertheme#1.sty}{\usetheme{#1}}{\usetheme{default}}}
\providecommand{\liliaSafeUseColorTheme}[1]{\IfFileExists{beamercolortheme#1.sty}{\usecolortheme{#1}}{}}
\providecommand{\liliaSafeUseFontTheme}[1]{\IfFileExists{beamerfonttheme#1.sty}{\usefonttheme{#1}}{}}
\providecommand{\liliaSafeUseInnerTheme}[1]{\IfFileExists{beamerinnertheme#1.sty}{\useinnertheme{#1}}{}}
\providecommand{\liliaSafeUseOuterTheme}[1]{\IfFileExists{beamerouterthemeH#1.sty}{\useoutertheme{#1}}{}}% typo-safe companion kept for back-compat
\providecommand{\liliaSafeUseOutertheme}[1]{\IfFileExists{beamerouthertheme#1.sty}{\useoutertheme{#1}}{}}
\makeatother
";

    /// <summary>
    /// Shims for CV-class-specific commands (moderncv, altacv, simplehipstercv,
    /// twentysecondcv, curve). Renders as readable text so the CV's content
    /// survives the compile even when we can't honour the custom class.
    /// </summary>
    public const string CvShims = @"% Shims for common CV-class commands (defined only if missing)
\makeatletter
% Personal info — render to printable text so the content is visible.
\providecommand{\name}[2]{\noindent\textbf{\LARGE #1 #2}\par\medskip}
\providecommand{\givenname}[1]{}
\providecommand{\familyname}[1]{}
\providecommand{\born}[1]{\textit{Born:} #1 \par}
\providecommand{\address}[3]{\small #1, #2, #3 \par}
\providecommand{\phone}[2][]{\small \textsuperscript{\textperiodcentered} #2 \par}
\providecommand{\email}[1]{\small \texttt{#1} \par}
\providecommand{\homepage}[1]{\small \url{#1} \par}
\providecommand{\extrainfo}[1]{\small \textit{#1} \par}
\providecommand{\quote}[1]{\begin{quote}\small\textit{#1}\end{quote}}
\providecommand{\social}[2][]{\small [#1] #2 \par}
\providecommand{\photo}[3][]{}
% Style / theme directives — drop silently.
\providecommand{\moderncvcolor}[1]{}
\providecommand{\moderncvstyle}[1]{}
\providecommand{\nopagenumbers}{}
\providecommand{\makecvtitle}{}
\providecommand{\cvtheme}[2][]{}
% Structural CV entries — render as readable paragraphs.
\providecommand{\cvsection}[1]{\par\medskip\noindent\textbf{\Large #1}\par\smallskip}
\providecommand{\cvitem}[3][]{\noindent\textbf{#2:} #3\par}
\providecommand{\cvitemwithcomment}[4][]{\noindent\textbf{#2:} #3 \emph{(#4)}\par}
\providecommand{\cvlistitem}[2][]{\noindent $\bullet$\ #2\par}
\providecommand{\cvlistdoubleitem}[3][]{\noindent $\bullet$\ #2 \quad $\bullet$\ #3\par}
\providecommand{\cventry}[7][]{\noindent\textbf{#3}, \emph{#4} (#5) \hfill #2\par #7\par\medskip}
\providecommand{\cvdoubleitem}[5][]{\noindent\textbf{#2:} #3 \quad \textbf{#4:} #5\par}
\providecommand{\cvline}[3][]{\noindent #2 \dotfill #3\par}
\providecommand{\cvcolumns}[1]{#1}
\providecommand{\cvcolumn}[3][]{\textbf{#2}: #3}
\providecommand{\cvkeyvaluelist}[2][]{}
% Resume.cls / custom resume macros.
\providecommand{\resumeSection}[1]{\par\medskip\noindent\textbf{\Large #1}\par\smallskip}
\providecommand{\resumeItem}[1]{\noindent $\bullet$\ #1\par}
\providecommand{\resumeSubItem}[1]{\quad $-$\ #1\par}
\providecommand{\resumeEntry}[4][]{\noindent\textbf{#2} \hfill #3\par \textit{#4}\par}
\makeatother
";

    /// <summary>
    /// Shims for journal-class-specific commands and environments so documents
    /// imported from mnras / pnas / frontiers / etc. can still compile under
    /// the fallback `article` class. Each shim is a no-op or minimal rendering
    /// that won't crash the compiler.
    /// </summary>
    public const string JournalShims = @"% Shims for common journal-class commands (defined only if missing)
\makeatletter
\providecommand{\affiliation}[1]{}
\providecommand{\affil}[2][]{#2}
\providecommand{\orcidlink}[1]{}
\providecommand{\orcid}[1]{}
\providecommand{\keywords}[1]{\paragraph*{Keywords:} #1}
\providecommand{\email}[1]{\texttt{#1}}
\providecommand{\corresp}[1]{#1}
\providecommand{\address}[1]{#1}
\providecommand{\inst}[1]{#1}
\providecommand{\institute}[1]{#1}
\providecommand{\titlerunning}[1]{}
\providecommand{\authorrunning}[1]{}
\providecommand{\offprints}[1]{}
\providecommand{\thanks}[1]{\footnote{#1}}
\@ifundefined{keywords}{\newenvironment{keywords}{\paragraph*{Keywords:}}{}}{}
\@ifundefined{affiliations}{\newenvironment{affiliations}{}{}}{}
\@ifundefined{methods}{\newenvironment{methods}{\section*{Methods}}{}}{}
\@ifundefined{highlights}{\newenvironment{highlights}{\paragraph*{Highlights}}{}}{}
\makeatother
";

    /// <summary>
    /// Shims for newsletter / newspaper-class commands. The `newspaper` package
    /// depends on `yfonts.sty` (Fraktur) which isn't on vanilla texlive, so we
    /// skip loading it and no-op the macros it would have defined. Same arc as
    /// JournalShims — accept visual degradation, keep compile green.
    /// </summary>
    public const string NewspaperShims = @"% Shims for newspaper-package commands (defined only if missing)
\providecommand{\SetPaperName}[1]{}
\providecommand{\SetHeaderName}[1]{}
\providecommand{\SetPaperLocation}[1]{}
\providecommand{\SetPaperSlogan}[1]{}
\providecommand{\SetPaperPrice}[1]{}
\providecommand{\currentvolume}[1]{}
\providecommand{\currentissue}[1]{}
";

    /// <summary>
    /// Shims for Overleaf-custom calendar packages (OL-calendar-mods.sty).
    /// These are defined locally inside Overleaf and aren't on texlive, so any
    /// \renewcommand{\SundayColor}{...} etc. in imported bodies would crash on
    /// undefined control sequence. Provide defaults so the renewcommand targets
    /// exist; calendar rendering itself falls back to tikz/calendar defaults.
    /// </summary>
    public const string CalendarShims = @"% Shims for Overleaf calendar-template customisations
\providecommand{\SundayColor}{red}
\providecommand{\SaturdayColor}{red}
\providecommand{\monthcolor}{black}
\providecommand{\watermarkfile}{}
\providecommand{\dhlist}{}
";

    /// <summary>
    /// Wraps a LaTeX fragment in a minimal document for per-block validation.
    /// </summary>
    public static string WrapForValidation(string latexFragment) =>
        WrapForValidation(latexFragment, LatexEngine.Pdflatex);

    /// <summary>
    /// Engine-aware wrapper — adds fontspec when validating against a
    /// block detected as lua/xelatex, so `\setmainfont{Charter}` in the
    /// fragment compiles instead of erroring with "Undefined control
    /// sequence". The wrapper preamble mirrors the production preamble
    /// addendum from <see cref="EngineAddendum"/>.
    /// </summary>
    public static string WrapForValidation(string latexFragment, LatexEngine engine)
    {
        return $@"\documentclass{{article}}
{ValidationPackages}
{EngineAddendum(engine)}{TheoremEnvironments}
\begin{{document}}
{latexFragment}
\end{{document}}";
    }
}
