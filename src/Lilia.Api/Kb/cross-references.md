---
slug: cross-references
title: Cross-references and labels
summary: Label a block, then refer to it — Lilia resolves and renumbers automatically on compile.
skill: lilia-tutor
audience: beginner
tags: [basics, references, labels]
keywords: cross reference label ref eqref equation figure table theorem number renumber autoref cleveref
---
# Cross-references and labels

**The idea.** Give a block a `label`, then refer to it by that label anywhere. Lilia resolves the number on compile and **renumbers automatically** when you move or insert things — you never hand-number.

**Label conventions.**
- Equations: `eq:…` → `Equation~(eq:loss)`
- Figures: `fig:…` → `Figure~(fig:arch)`
- Tables: `tab:…` → `Table~(tab:acc)`
- Theorems: `thm:…` → `Theorem~(thm:main)`

**Example.**
```lml
@equation[mode=display, label="eq:loss"]
  \mathcal{L} = \lVert y - \hat{y} \rVert_2^2
```
Then in a paragraph: `Minimizing Equation~(eq:loss) ...`. Move the equation and the reference still points to the right number.

**Tips.** Use a consistent prefix per type so labels stay readable. A reference that shows as `??` after compile means the label doesn't exist (typo or deleted block). Related: blocks-and-lml, equations.
