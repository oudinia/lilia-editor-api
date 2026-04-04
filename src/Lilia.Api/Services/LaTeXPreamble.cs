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

% Lists
\usepackage{enumitem}

% Code
\usepackage{listings}

% Algorithms & callouts
\usepackage{algorithm}
\usepackage{algorithmic}
\usepackage{tcolorbox}

% Text formatting
\usepackage{soul}
\usepackage{ulem}
\normalem

% Links & references (hyperref before cleveref)
\usepackage{url}
\usepackage[colorlinks=true,linkcolor=blue,citecolor=blue,urlcolor=blue]{hyperref}
\usepackage[nameinlink]{cleveref}
";

    /// <summary>
    /// Packages for per-block validation — uses [demo] graphicx so missing images don't fail.
    /// </summary>
    public const string ValidationPackages = @"% Encoding & fonts
\usepackage[utf8]{inputenc}
\usepackage[T1]{fontenc}
\usepackage{textcomp}

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

% Lists
\usepackage{enumitem}

% Code
\usepackage{listings}

% Algorithms & callouts
\usepackage{algorithm}
\usepackage{algorithmic}
\usepackage{tcolorbox}

% Text formatting
\usepackage{soul}
\usepackage{ulem}
\normalem

% Links & references
\usepackage{url}
\usepackage[colorlinks=true]{hyperref}
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
