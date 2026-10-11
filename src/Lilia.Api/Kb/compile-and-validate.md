---
slug: compile-and-validate
lang: en
title: Check that it compiles and fix errors
summary: Validate a document, read its errors and warnings, fix them with Ask Lilia, and check again.
skill: lilia-compile-doctor
audience: all
tags: [compile, errors, validation, latex]
keywords: compile error validate validation check warnings fix issues latex missing dollar undefined control sequence package brace math mode doctor pdf build
---
# Check that it compiles and fix errors

## Steps {#steps}
1. In the rail on the editor's left, click the shield (**Validate**), then **Validate now** (or **Re-run validation** if it has run before).
   ![Validate now in the Validate panel](/help/img/compile-and-validate/en-1.png)
2. Read the result: **errors** stop the PDF; **warnings** let it build but deserve a look. Each one names the block it comes from when it can.
3. Fix an error: select its block, then ask **Ask Lilia** *"Why won't this compile? Fix this error"* and apply what it proposes.
4. Click **Re-run validation** until the panel says the document compiled with no errors.
   ![A clean validation](/help/img/compile-and-validate/en-2.png)

Elsewhere: in **Documents**, the **Has issues** filter lists every document with errors or warnings, and each card offers **Fix issues**. On a phone: **⋯ → Check document**, then **Ask Lilia to fix** on an issue.

## What this means {#beginner}
- **Compiling** is how a LaTeX paper becomes a PDF. Lilia does it for you; *validating* is doing it on purpose to see whether anything goes wrong.
- An **error** is something the PDF cannot be built with, often one missing character. A **warning** is a note: the PDF builds, but something may look wrong (a missing reference, for example).
- Saving does not check your paper. *Not validated yet* only means no check has run.
- Errors are normal and usually small. Fix the first one and check again: later errors often disappear with it.

## For LaTeX users {#expert}
Common causes:
- `Missing $ inserted`: math (`_`, `^`) outside math mode. Wrap it: `$x_i$`.
- `Undefined control sequence`: a command Lilia or LaTeX doesn't know, or a missing package (add it from *LaTeX packages* in the command palette, behind the **?** button).
- `Runaway argument` / `Missing } inserted`: an unbalanced brace.

Fix the **first** error and recompile before chasing the rest; later ones are often cascades. To see what Lilia actually emits, open the LaTeX view (split view or **View LaTeX** on a phone). The compile doctor skill (*"Why won't this compile?"*) reads the error and the block and proposes the smallest fix.
