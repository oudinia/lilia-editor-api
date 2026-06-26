---
slug: compile-and-validate
title: Compiling and fixing errors
summary: How Lilia validates blocks as you write and how to read a compile error when it happens.
skill: lilia-compile-doctor
audience: intermediate
tags: [compile, errors, validation, latex]
keywords: compile error validate latex missing dollar undefined control sequence package brace math mode fix doctor pdf build
---
# Compiling and fixing errors

**Validation as you write.** Lilia checks each block against the LaTeX catalog (known commands, packages, document classes) and flags problems contextually — an unknown command, an unbalanced brace, a math symbol outside math mode — before you compile.

**Reading a compile error.** When a build fails, the message usually names a cause and a line:
- `Missing $ inserted` → math (like `_` or `^`) used outside `$…$`. Wrap it: `$x_i$`.
- `Undefined control sequence` → a command Lilia/LaTeX doesn't know, or a missing package.
- `Runaway argument` / `Missing } inserted` → an unbalanced brace `{ }`.

**Workflow.**
1. Read the first error — later ones are often cascades of the first.
2. Jump to the named block, fix the one cause.
3. Re-validate; the inline check clears when it's right.

**Tips.** Fix the **first** error and recompile before chasing the rest. If a command needs a package, add it; Lilia's coverage report shows what's supported. Related: equations, blocks-and-lml, the `lilia-compile-doctor` skill.
