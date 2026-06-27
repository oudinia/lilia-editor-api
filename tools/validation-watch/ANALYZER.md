# Validation-watch analyzer playbook

How to read and classify `block_validations` errors pulled by
`pull-errors.sh`, and what action each class implies. The cron analyzer
follows this; extend it as new error classes appear.

Each block is validated by compiling it **standalone** — the harness wraps the
block's rendered LaTeX in a minimal document (preamble + `\begin{document}` …
`\end{document}`) and runs `pdflatex`. So a failure is either (a) the wrapped
content is malformed, (b) the harness preamble is missing a package/counter the
content needs, or (c) the content was never meant to be wrapped (already a full
doc). Distinguishing these three is the core of the analysis.

## Triage axis: pipeline/validator bug vs. user content

- **Pipeline/validator bug** — the block content is reasonable but the harness
  mishandles it (missing preamble package, double-wrap, unsupported language,
  unhandled Unicode). These are **actionable on our side** → flag for an API/
  exporter fix. Highest priority: they fail *every* user with that content.
- **User content** — genuinely broken LaTeX the user typed. Surface as a count;
  not our bug, but high volume may hint at a UX/affordance gap.

## Known error classes

### 1. `Two \documentclass or \documentstyle commands` — PIPELINE BUG
The wrapped content already contains its own `\documentclass`/full preamble, so
the harness double-wraps. Usually an **imported** document whose blocks retained
full-document fragments, or a block storing a complete doc.
- **Fix:** validator should detect a leading `\documentclass` in the block's
  rendered source and skip its own wrapper (or strip the inner preamble).
- **Signal:** many rows sharing one `document_id` → that doc's blocks are
  contaminated; check the import path.

### 2. `No counter 'X' defined` (e.g. `'Theorem'`) — PIPELINE BUG
A theorem-family block renders `\begin{Theorem}…` but the harness preamble never
declared `\newtheorem{Theorem}{Theorem}`. Note **casing**: block content uses
`theoremType:"Theorem"` (capitalized) → the env name is capital `Theorem`, but
amsthm convention is lowercase. Mismatch = undefined counter.
- **Fix:** theorem exporter must emit `\newtheorem{<envName>}{<label>}` matching
  the exact env it generates, or normalize `theoremType` casing to a known set
  (theorem/lemma/proof/definition/corollary…).

### 3. `Unicode character <c> (U+XXXX)` — PIPELINE GAP (mostly)
Paragraph prose contains literal Unicode (Greek γ Δ μ Λ, sub/superscripts ₀ ²)
instead of math commands. pdflatex without unicode handling chokes.
- **Fix (preferred):** on save/export, convert common literal Unicode in *text*
  to LaTeX (γ→`$\gamma$`, Δ→`$\Delta$`, ₀→`$_0$`). Extend the existing
  font-shim / pass-through Unicode coverage (see migration
  `SeedPassThroughEnvsAndFontShimCoverage`).
- **Fix (fallback):** validator preamble adds `\usepackage{textgreek}` +
  `\newunicodechar` maps under pdflatex, or routes such blocks to a
  Unicode-aware engine (lualatex/xelatex).

### 4. `Package Listings Error: Couldn't load requested language` — PIPELINE BUG
A `code` block's `language=` isn't a listings-supported language.
- **Fix:** map editor language ids → listings names; fall back to no `language`
  (plain verbatim) when unknown instead of failing.

### 5. Anything else — CLASSIFY
Capture the `! …` line, the block type, and a content snippet. Decide
pipeline-vs-user by the axis above. If pipeline, name the concrete fix.

## Output contract

Append one dated section to `reports/validation-analysis.md`:
- Headline counts (new errors this run, by class).
- For each class: count, pipeline-vs-user, root cause, concrete fix, and the
  worst-offender `document_id` if concentrated.
- A short **ACTION** list of API/exporter fixes worth opening, most impactful
  first. If a class is already noted in a prior run, say "still open" + the new
  count rather than repeating the full analysis.
