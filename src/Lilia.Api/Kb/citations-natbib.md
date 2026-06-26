---
slug: citations-natbib
title: Citations with natbib
summary: Add BibTeX entries to @bibliography and cite them with \citep / \citet — Lilia's default citation backend.
skill: lilia-citations
audience: intermediate
tags: [citations, natbib, bibtex, bibliography]
keywords: citation natbib bibtex citep citet cite bibliography references author year parenthetical textual cite key
---
# Citations with natbib

**The backend.** Lilia uses `natbib` + BibTeX by default — the most journal-compatible option for v1. Your references live in a `@bibliography` block; you cite them by key.

**The two you need.**
- `\citep{key}` → parenthetical: *(Vaswani et al., 2017)*
- `\citet{key}` → textual: *Vaswani et al. (2017)*

**Workflow.**
1. Get a BibTeX entry — paste an identifier into the **DOI → BibTeX** tool, or write it.
2. Put it in your `@bibliography` block; note its cite key.
3. Cite inline with `\citep{...}` / `\citet{...}`.

```lml
@bibliography
  @article{vaswani2017attention,
    author = {Vaswani, Ashish and others},
    title  = {Attention Is All You Need},
    year   = {2017}
  }
```
Then: `Transformers \citep{vaswani2017attention} changed sequence modeling.`

**Tips.** Keep cite keys stable — changing one breaks every citation that used it. `biblatex` is deferred to a later release; author in `natbib` for now. Related: doi-to-bibtex, the `lilia-citations` skill.
