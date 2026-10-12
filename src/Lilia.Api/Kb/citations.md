---
slug: citations
lang: en
title: Add references and cite them
summary: Build the paper's bibliography from a DOI, an arXiv id or BibTeX, and cite entries where you write.
skill: lilia-citations
audience: all
tags: [citations, bibliography, references, bibtex, doi]
keywords: cite citation reference references bibliography bibtex doi arxiv isbn add source paper cite key natbib footnote
---
# Add references and cite them

## Steps {#steps}
1. In the rail on the left, click the book (**Bibliography**).
   ![The Bibliography panel](/help/img/citations/en-1.png)
2. Add a source: search a paper by title, enter an arXiv id, use **Add Entry** to fill it in, or **Import BibTeX** to bring in a whole `.bib`.
3. Cite it: place the cursor where the citation goes and click **Cite** on the entry. Or type **@** in your text and pick it from the list.
4. Or ask **Ask Lilia** *"Cite a paper from its DOI or arXiv id"* with the identifier.
5. The bibliography prints at the end of the paper, in the citation style you choose in the panel.

## What this means {#beginner}
- A **reference** is an entry in your bibliography (author, title, year…). A **citation** is where you point to it in the text, e.g. "(Smith, 2020)".
- You add a reference once and cite it as often as you need; the list at the end is built for you.

## For LaTeX users {#expert}
- Citations use natbib with BibTeX: **Cite** inserts a parenthetical citation (`\citep{key}`; textual `\citet`, `\citeauthor` and `\citeyear` are kept on import); the cite key is shown on each entry and can be edited.
- **@** opens the reference picker: bibliography entries for citations, labelled blocks for `\cref` cross-references.
- The `.bib` travels with the LaTeX export.
