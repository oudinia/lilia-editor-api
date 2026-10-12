---
slug: document-look
lang: en
title: Choose your paper's look
summary: Set the document's type and class, its page layout, and its theme — Classic, Cerulean, Index, Carnet, Gazette or Exposition.
skill: lilia-tutor
audience: all
tags: [look, theme, layout, class, page settings]
keywords: look theme style design classic cerulean index carnet gazette exposition paper layout margins columns font size page numbering header footer document class article report book slides
---
# Choose your paper's look

## Steps {#steps}
1. In the document, click the type button in the top bar (for example **Article ▾**): its settings open.
   ![The document's settings, with Look](/help/img/document-look/en-1.png)
2. **Type & class**: article, report, book, slides…
3. **Class options**: font size, paper, columns, one or two sides.
4. **Layout**: margins, line spacing, paragraph indent, page numbering, header and footer text.
5. **Look**: the **Document theme**, the paper (*Theme's paper* or *White always*), section colours and the tables' style.

The themes:
- **Classic**: submissions, and anything with a publisher class.
- **Cerulean**: reports, theses, lab handbooks.
- **Index**: course notes, lecture series, handbooks.
- **Carnet**: essays, humanities, long reading on screen.
- **Gazette**: newsletters, posters, outreach (never a paper).
- **Exposition**: slides and posters on screen (slides only).

## What this means {#beginner}
- The look is how the PDF will look: fonts, colours, spacing. Your text doesn't change.
- Sending to a journal? Keep **Classic**: journals apply their own style.

## For LaTeX users {#expert}
- Type & class set `\documentclass`; class options are its options. A themed document loads `lilia-theme.sty` (or a beamer theme for slides); the LaTeX export includes the package.
- With a publisher class (e.g. IEEEtran, acmart), keep Classic: the venue's class sets the style.
