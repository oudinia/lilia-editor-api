---
slug: doi-to-bibtex
title: DOI / ISBN / arXiv → BibTeX
summary: Turn an identifier or a messy reference into a clean, citable BibTeX entry with a stable cite key.
tool: doi-to-bibtex
skill: lilia-citations
audience: all
tags: [citations, bibtex, doi, references]
keywords: doi isbn arxiv url reference citation bibliography natbib citep citet bibtex cite key crossref
---
# DOI / ISBN / arXiv → BibTeX

**What it does.** Paste a DOI (`10.1103/PhysRev.47.777`), an arXiv id (`arXiv:1706.03762`), an ISBN, or a URL and Lilia resolves it to a formatted BibTeX entry with a sensible cite key. A pasted messy reference string is cleaned up the same way.

**When to use it.** You have an identifier (or a half-remembered reference) and want a reference you can cite immediately, instead of typing BibTeX by hand.

**How.**
1. Open the **DOI → BibTeX** tool (free standalone, or inside the editor).
2. Paste one identifier per line.
3. Copy the BibTeX, or **Open in the editor** to drop it into your document's `@bibliography` block.

**What you get.**
```bibtex
@article{vaswani2017attention,
  author  = {Vaswani, Ashish and others},
  title   = {Attention Is All You Need},
  year    = {2017},
  eprint  = {1706.03762},
  archivePrefix = {arXiv}
}
```

**Cite it.** With `natbib`: `\citet{vaswani2017attention}` (textual) or `\citep{...}` (parenthetical). The entry lives in your `@bibliography` block; the citation renders on compile.

**Tips.** Always sanity-check author/year/venue against the source record — resolvers can be incomplete. Copying the BibTeX source is always free; metering only applies to AI-assisted formatting. Related: cross-references, the `lilia-citations` skill.
