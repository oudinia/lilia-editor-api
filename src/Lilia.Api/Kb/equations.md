---
slug: equations
title: Writing equations
summary: Inline math vs display @equation blocks, with labels for cross-referencing.
skill: lilia-equation
audience: beginner
tags: [math, equations, latex]
keywords: equation math inline display align label number latex amsmath formula symbol matrix cases
---
# Writing equations

**Two places math lives.**
- **Inline** — inside a paragraph with `$…$`: `the loss $\mathcal{L}$ is convex`.
- **Display** — a standalone `@equation` block, optionally numbered and labelled.

**Display block.**
```lml
@equation[mode=display, label="eq:softmax"]
  \sigma(z)_i = \frac{e^{z_i}}{\sum_j e^{z_j}}
```
Reference it with `Equation~(eq:softmax)`. Multi-line? Use an `align`-style body with `&` alignment and `\\` line breaks inside the block.

**Common building blocks.** `\frac{a}{b}`, `\sum_{i=1}^{n}`, `\int`, `\mathbf{x}`, `\hat{y}`, `\lVert W \rVert`, matrices with `\begin{bmatrix}…\end{bmatrix}`, cases with `\begin{cases}…\end{cases}`.

**Tips.** Give equations you'll reference a `label`; leave throwaway math inline. If a symbol won't compile, it's usually a missing package or a stray `$` — see compile-and-validate. Related: cross-references, the `lilia-equation` skill.
