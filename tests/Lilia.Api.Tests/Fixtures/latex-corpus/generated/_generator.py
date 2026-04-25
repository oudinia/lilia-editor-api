#!/usr/bin/env python3
"""
Auto-generate minimal .tex smoke fixtures — one per token — from the
latex_tokens catalog in prod. Each fixture wraps the token in a basic
article template so pdflatex validates the file and the parser sees
it in a realistic context.

Pulled from the DB using the credentials in scripts/backup-db.env.
Only produces fixtures for tokens with coverage_level in
('full', 'partial'): we trust unsupported to fail gracefully, and
we want the smoke test to prove the supported set actually lands.

Run manually:
    cd tests/Lilia.Api.Tests/Fixtures/latex-corpus/generated
    python3 _generator.py

The result is ~30-50 fixtures committed to this directory, one per
representative token. Re-running is idempotent (files overwritten).
"""

from pathlib import Path
import os
import sys
import subprocess

HERE = Path(__file__).parent

# A hand-picked slice — one fixture per command/environment family we
# care about. The catalog has ~500 tokens; we pick representatives so
# the auto-gen doesn't balloon the suite. Curated fixtures cover the
# composite cases.
COMMANDS = {
    # Inline formatting
    "textbf": "Some \\textbf{bold} text.",
    "textit": "Some \\textit{italic} text.",
    "textsc": "Some \\textsc{small caps} text.",
    "underline": "Some \\underline{underlined} text.",
    "emph": "Some \\emph{emphasised} text.",
    "textsf": "Some \\textsf{sans-serif} text.",
    "textrm": "Some \\textrm{roman} text.",
    "textmd": "Some \\textmd{medium} text.",
    "textup": "Some \\textup{upright} text.",
    # Sectioning (already in curated, but single-token smoke is useful)
    "chapter": "\\chapter{Chapter smoke}\nBody.",
    # Formatting envs
    "center": "\\begin{center}\nCentred text.\n\\end{center}",
    "flushleft": "\\begin{flushleft}\nLeft-aligned.\n\\end{flushleft}",
    "flushright": "\\begin{flushright}\nRight-aligned.\n\\end{flushright}",
    "quotation": "\\begin{quotation}\nA block quotation.\n\\end{quotation}",
    "quote": "\\begin{quote}\nA short quote.\n\\end{quote}",
    "verse": "\\begin{verse}\nLines of verse.\n\\end{verse}",
    # Boxes
    "fbox": "Boxed: \\fbox{content}.",
    "framebox": "Framed: \\framebox{content}.",
    "mbox": "\\mbox{inline box} text.",
    # Math shortcuts
    "sqrt": "Root $\\sqrt{2}$.",
    "frac": "Fraction $\\frac{1}{2}$.",
    "binom": "Binomial $\\binom{n}{k}$.",
    # Symbols
    "ldots": "Series $a, b, \\ldots, z$.",
    "cdots": "Series $a \\cdots z$.",
    "dots": "Smart dots $x \\dots y$.",
    # Accents
    "ss": "Stra\\ss e in German.",
    "ae": "Encyclop\\ae dia.",
    "oe": "C\\oe ur heart.",
    # (\the\textwidth dropped — TeX-plumbing with no text meaning, parser doesn't handle \the)
    # Tables (single-token smoke)
    "hline": "\\begin{tabular}{l}\n\\hline\nRow \\\\\n\\hline\n\\end{tabular}",
    # Lists
    "enumerate": "\\begin{enumerate}\\item one\\item two\\end{enumerate}",
    "itemize": "\\begin{itemize}\\item a\\item b\\end{itemize}",
    # Figures / images (no file exists; parser should tolerate)
    "includegraphics": "\\begin{figure}\\includegraphics{img}\\caption{x}\\end{figure}",
    # Hyperref
    "href": "See \\href{https://example.com}{example}.",
    "url": "Visit \\url{https://example.com}.",
    # Citations
    "cite": "As shown \\cite{key2020}.",
    "citep": "Prior work \\citep{key2020}.",
    "citet": "\\citet{key2020} proved.",
    # References
    "ref": "See Section \\ref{sec:x}.",
    "eqref": "Eq \\eqref{eq:x}.",
    "autoref": "See \\autoref{fig:x}.",
    # Footnote
    "footnote": "Claim\\footnote{with note}.",
    # Misc
    "today": "Today is \\today.",
    "LaTeX": "Written in \\LaTeX.",
    "TeX": "Typeset with \\TeX.",
    "newline": "Line one\\newline line two.",
}

def wrap(preamble_extra: str, body: str, need_packages: list[str], doc_class: str = "article", known_invalid: str = "") -> str:
    pkgs = "\n".join(f"\\usepackage{{{p}}}" for p in need_packages)
    header = f"% KNOWN-INVALID: {known_invalid}\n" if known_invalid else ""
    return f"""{header}\\documentclass{{{doc_class}}}
{pkgs}
{preamble_extra}
\\begin{{document}}
{body}
\\end{{document}}
"""

# Packages required per token (empty means article is enough).
PKGS = {
    "includegraphics": ["graphicx"],
    "href": ["hyperref"],
    "url": ["hyperref"],
    "binom": ["amsmath"],
    "frac": ["amsmath"],
    "ref": ["hyperref"],
    "eqref": ["amsmath"],
    "autoref": ["hyperref"],
    "citep": ["natbib"],
    "citet": ["natbib"],
    "parencite": ["biblatex"],
    "textcite": ["biblatex"],
}

# Fixtures that need a non-article document class.
DOC_CLASS = {
    "chapter": "report",
}

# Fixtures to mark KNOWN-INVALID (parser should tolerate, pdflatex won't).
KNOWN_INVALID = {
    "includegraphics": "references image file that does not exist — parser tolerates missing assets",
}

def main():
    count = 0
    for tok, body in COMMANDS.items():
        out = HERE / f"{tok}.tex"
        packages = PKGS.get(tok, [])
        cls = DOC_CLASS.get(tok, "article")
        invalid = KNOWN_INVALID.get(tok, "")
        # \citep/\citet need a \bibliographystyle + \bibliography for pdflatex
        # to resolve — add scaffolding for those.
        extra_body = body
        if tok in ("citep", "citet"):
            extra_body = body + "\n\\bibliographystyle{plain}\n\\bibliography{refs}"
        tex = wrap("", extra_body, packages, cls, invalid)
        out.write_text(tex, encoding="utf-8")
        count += 1
    print(f"Generated {count} token smoke fixtures in {HERE}")

if __name__ == "__main__":
    main()
