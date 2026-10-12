---
slug: import
lang: en
title: Bring in a paper (.tex, Word, ePub)
summary: Import a LaTeX file or Overleaf project, a Word document or an ePub, review it, and turn it into a Lilia document.
skill: lilia-tools
audience: all
tags: [import, latex, word, docx, epub, overleaf]
keywords: import upload bring in open tex latex zip overleaf word docx epub convert review paste existing paper
---
# Bring in a paper (.tex, Word, ePub)

## Steps {#steps}
1. Open **Import / Export** (Home, or the Lilia menu) and stay on its import tab.
2. Drop your file(s) on the box, or click it to browse: `.tex`, `.zip` (Overleaf), `.docx`, `.epub`, `.lilia`, `.lml`.
   ![The import box](/help/img/import/en-1.png)
3. Keep **Review before importing** ticked to check the result first, then click **Import**.
4. On the review page, look through the blocks and any issues, then click **Import**: the paper opens in the editor.

Already in a document? To bring in a piece of LaTeX, use **Paste LaTeX** from the command palette (the **?** button in the top bar).

## What this means {#beginner}
- Lilia turns your file into blocks (headings, paragraphs, equations, figures…) so you can edit it like any Lilia document.
- The review step shows what came across and what needs attention before anything is created.
- Your original file isn't changed.

## For LaTeX users {#expert}
- Unknown environments and commands are flagged in the review; most standard packages are recognised (see the coverage report).
- An Overleaf `.zip` is read as a project: its main `.tex`, its images and its `.bib`.
- Word headings, lists, tables, equations and captions map to blocks; styles that have no LaTeX meaning are dropped.
