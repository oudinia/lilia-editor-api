---
slug: getting-started
lang: en
title: Write your first paper
summary: From a blank page to a PDF — create a document, write, add a figure and a citation, check it, download it.
skill: lilia-tutor
audience: beginner
tags: [basics, onboarding, getting-started]
keywords: getting started first paper new document begin write figure citation check pdf download beginner start tutorial
---
# Write your first paper

## Steps {#steps}
1. On **Home**, click **New paper** (or in **Documents**, click **New**). Choose **Blank document**, give it a title, and create it.
   ![The New document chooser](/help/img/getting-started/en-1.png)
2. The document opens beside **Ask Lilia**. Click in the page and write. Each paragraph, heading or list is a **block**. In an empty line, type **/** to add a heading, an equation, a figure or a table.
   ![The block menu after typing /](/help/img/getting-started/en-2.png)
3. Add a figure: **/** → *Figure*, upload your image, then write its caption under it.
4. Cite a paper: in **Ask Lilia** (the conversation beside the document) ask *"Cite a paper from its DOI or arXiv id"* with the DOI. Lilia gives you the reference and the citation to apply.
5. Check it: in the rail on the left, click the shield (**Validate**), then **Validate now**. If it lists errors, see *Check that it compiles and fix errors*.
   ![The Validate panel](/help/img/getting-started/en-3.png)
6. Download it: **Export** (top bar) → **PDF**.

Your work saves as you type. To keep a named copy of this moment, hover the save indicator and click **Save a version** (⌘S / Ctrl+S).

## What this means {#beginner}
- Lilia writes **LaTeX** for you: the language most journals and universities use to typeset papers. You write as in a word processor; Lilia turns it into a properly typeset PDF.
- A **block** is one piece of your paper: a paragraph, a heading, an equation, a figure. You can move it, change its type or delete it on its own.
- **Validate** means: Lilia builds the PDF and tells you if anything stops it. Errors are normal while writing, and each one points to the block to fix.
- Stuck? Ask Lilia in your own words, in English, French or Spanish.

## For LaTeX users {#expert}
- Each block becomes LaTeX: heading → `\section`, equation → `equation`/`align`, figure → `figure` with `\includegraphics` and `\caption`, citation → `\citep`/`\citet` (natbib + BibTeX).
- The document class and its options open from the type button in the top bar, e.g. **Article ▾** (*Type & class*, *Class options*, *Layout*, *Look*). Lilia manages the preamble; the **?** button in the top bar opens the command palette (*LaTeX packages*, *Paste LaTeX*).
- **Settings → Editor defaults → LaTeX-native editing** shows commands and source while you write. **Export → LaTeX** gives one `.tex` or a `.zip` project (flat or Overleaf-style).
