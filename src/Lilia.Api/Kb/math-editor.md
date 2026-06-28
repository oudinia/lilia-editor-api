---
slug: math-editor
title: The math editor — buttons, raw LaTeX, AI, and keyboard shortcuts
summary: Three ways to write math in Lilia's equation editor (buttons, editable raw LaTeX, AI describe-to-LaTeX) plus the full keyboard-shortcut layer.
skill: lilia-equation
audience: beginner
tags: [math, equations, math-editor, shortcuts, keyboard, latex]
keywords: math editor equation shortcut shortcuts keyboard hotkey fraction superscript subscript exponent power integral sum symbol structure structures raw latex source tab ai describe generate quadratic matrix palette command backslash caret underscore type write how do i preview
---
# The math editor

Open it by adding or editing an **equation block**. Everything you build is one equation with a **live preview** beside the editor — and there are **three ways to enter math**, all feeding the same equation:

## 1. Buttons (discover)
The drawer has two tabs of clickable tiles:
- **Symbols** — Greek, operators, relations, arrows, set/logic symbols. Each tile shows its `\command` caption so you learn the LaTeX as you click.
- **Structures** — fractions, roots, integrals, sums/products, matrices, cases, big delimiters, accents. Click to drop the scaffold, then fill the slots.

Best when you don't remember the LaTeX name for something.

## 2. Raw LaTeX (speed) — the Source tab
The **Source tab** is an editable LaTeX field. Type LaTeX directly — e.g. `c^2 = a^2 + b^2` or `\frac{-b \pm \sqrt{b^2-4ac}}{2a}` — and the preview and the buttons stay in sync as you type (it round-trips through the editor's structure model). Best when you already know the LaTeX.

## 3. AI (describe it) — the "Describe the math" box
At the top of the Source tab, type a plain-language description and press **Generate**:
- "gaussian integral from 0 to infinity" → `\int_0^{\infty} e^{-x^2}\,dx = \frac{\sqrt{\pi}}{2}`
- "the quadratic formula" → `\frac{-b \pm \sqrt{b^2-4ac}}{2a}`
- "sum of i squared from 1 to n" → `\sum_{i=1}^{n} i^2`

The generated LaTeX drops straight into the editor; tweak it with the buttons or by hand. Best when you know the math but not the LaTeX.

## Keyboard shortcuts
Type directly in the editor — these work anywhere in the equation:

| Key | Does |
|---|---|
| `^` | superscript / exponent on the previous atom (`a` `^` `2` → `a²`) |
| `_` | subscript (`x` `_` `i` → `xᵢ`) |
| `\` | open the **command palette** — type a name to insert a symbol **or expand a structure** (`\frac` → fraction, `\sqrt`, `\int`, `\sum`, `\matrix`, `\binom`, `\cases`, `\vec`…), then Enter |
| `=` `+` `-` `*` `<` `>` | insert the operator / relation (`*` → ×, `-` → −) |
| letters / numbers | type straight into the equation |
| `(` `)` `[` `]` `,` `.` etc. | typed literally as delimiters/punctuation |
| `Tab` | move to the next slot of a structure (e.g. numerator → denominator); at the top level, cycle the drawer tabs |
| `Esc` | step out of the current structure slot |
| `Backspace` | delete the atom before the caret |
| `←` `→` | move the caret |
| `Shift`+`Enter` | new line (multi-line / aligned math) |
| `&` | alignment point (for aligned equations) |
| `Cmd/Ctrl`+`Z` | undo · `Cmd/Ctrl`+`Shift`+`Z` or `Cmd/Ctrl`+`Y` redo |

**Tip:** the fastest path is usually a mix — `\` to grab a structure or symbol, then type the contents, `^`/`_` for scripts, and the AI box when you'd rather describe than recall. Related: equations, the `lilia-equation` skill.
