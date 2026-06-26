---
slug: word-to-latex
title: Word (.docx) → LaTeX
summary: Import a Word document and get editable Lilia blocks — headings, paragraphs, math, tables — not a wall of raw LaTeX.
tool: word-to-latex
skill: lilia-tools
audience: all
tags: [import, docx, word, migration]
keywords: word docx import convert migrate manuscript heading paragraph equation table review blocks lml
---
# Word (.docx) → LaTeX

**What it does.** Upload a `.docx` and Lilia converts it into typed blocks (`@heading`, `@paragraph`, `@equation`, `@table`, …) — the same blocks you edit natively — so you continue in Lilia instead of fixing exported LaTeX.

**When to use it.** You started a manuscript in Word (or a co-author sent one) and want to move it into Lilia with structure intact.

**How.**
1. Open the **Word → LaTeX** tool (or import from inside the editor).
2. Upload the `.docx`.
3. Review the proposed blocks — accept, reject, or edit each — then finalize into a document.

**What you get.** A document of editable blocks. Word equations become `@equation` blocks; tables become `@table`; headings keep their levels. From there everything compiles to real LaTeX.

**Tips.** Complex equations and exotic Word formatting may need a touch-up — the review step is where you catch them. Track-changes and comments are not imported. Related: blocks-and-lml, compile-and-validate, the `lilia-tools` skill.
