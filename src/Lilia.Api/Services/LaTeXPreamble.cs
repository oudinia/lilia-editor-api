namespace Lilia.Api.Services;

/// <summary>
/// Single source of truth for the LaTeX preamble used across
/// full document export, per-block validation, and rendering.
/// </summary>
public static class LaTeXPreamble
{
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

% Typography
\usepackage{microtype}
\usepackage{setspace}
\usepackage{parskip}

% Graphics & floats
\usepackage{graphicx}
\usepackage{float}
\usepackage{caption}
\usepackage{subcaption}
\usepackage{xcolor}

% Tables
\usepackage{booktabs}
\usepackage{multirow}
\usepackage{tabularx}
\usepackage{longtable}
\usepackage{array}

% Lists
\usepackage{enumitem}

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

% Links & references (hyperref before cleveref).
% PassOptionsToPackage + plain usepackage avoids option-clash when an imported
% package (orcidlink, hyperxmp, etc.) pre-loaded hyperref with default options.
\PassOptionsToPackage{colorlinks=true,linkcolor=blue,citecolor=blue,urlcolor=blue}{hyperref}
\usepackage{url}
\usepackage{hyperref}
\usepackage[nameinlink]{cleveref}
";

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

% Typography
\usepackage{microtype}
\usepackage{setspace}
\usepackage{parskip}

% Graphics & floats (demo mode for validation — no real images needed)
\usepackage[demo]{graphicx}
\usepackage{float}
\usepackage{caption}
\usepackage{subcaption}
\usepackage{xcolor}

% Tables
\usepackage{booktabs}
\usepackage{multirow}
\usepackage{tabularx}
\usepackage{longtable}
\usepackage{array}

% Lists
\usepackage{enumitem}

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

% Links & references
\PassOptionsToPackage{colorlinks=true}{hyperref}
\usepackage{url}
\usepackage{hyperref}
\usepackage[nameinlink]{cleveref}
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
\providecommand{\liliaSafeUseOuterTheme}[1]{\IfFileExists{beamerouterthemeH#1.sty}{\useoutertheme{#1}}{}}
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
    /// Wraps a LaTeX fragment in a minimal document for per-block validation.
    /// </summary>
    public static string WrapForValidation(string latexFragment)
    {
        return $@"\documentclass{{article}}
{ValidationPackages}
{TheoremEnvironments}
\begin{{document}}
{latexFragment}
\end{{document}}";
    }
}
