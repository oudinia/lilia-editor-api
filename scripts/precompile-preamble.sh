#!/bin/bash
# Generate precompiled LaTeX preamble format file (.fmt)
# Run once at container startup or when preamble changes.
# Cuts ~1s off every pdflatex invocation by pre-loading all 31 packages.

set -euo pipefail

PREAMBLE_DIR="/tmp/lilia-latex-preamble"
mkdir -p "$PREAMBLE_DIR"

cat > "$PREAMBLE_DIR/lilia-preamble.tex" << 'PREAMBLE'
\documentclass{article}

% Encoding & fonts
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

% Theorem environments
\newtheorem{theorem}{Theorem}
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

\dump
PREAMBLE

echo "Compiling preamble format file..."
cd "$PREAMBLE_DIR"
pdflatex -ini -jobname=lilia-preamble "&pdflatex lilia-preamble.tex"

if [ -f "$PREAMBLE_DIR/lilia-preamble.fmt" ]; then
    echo "Precompiled preamble created: $PREAMBLE_DIR/lilia-preamble.fmt"
    ls -lh "$PREAMBLE_DIR/lilia-preamble.fmt"
else
    echo "ERROR: Failed to create precompiled preamble" >&2
    exit 1
fi
