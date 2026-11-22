---
slug: import
lang: en
title: Bring in a paper (.tex, Word, PDF, scans)
summary: Import a LaTeX file or Overleaf project, a Word document, an ePub, a PDF or a scan of handwritten notes, review it, and turn it into a Lilia document.
skill: lilia-tools
audience: all
tags: [import, latex, word, docx, epub, overleaf, pdf, scan, handwriting]
keywords: import upload bring in open tex latex zip overleaf word docx epub pdf scan photo image jpg png handwritten notes ocr convert review paste existing paper
---
# Bring in a paper (.tex, Word, PDF, scans)

## Steps {#steps}
1. Open **Import / Export** (Home, or the Lilia menu) and stay on its import tab.
2. Drop your file(s) on the box, or click it to browse: `.tex`, `.zip` (Overleaf), `.docx`, `.epub`, `.lilia`, `.lml`, `.md`, `.pdf`, and scans or photos of a page as `.jpg` or `.png`.
   ![The import box](/help/img/import/en-1.png)
3. Keep **Review before importing** ticked to check the result first, then click **Import**.
4. On the review page, look through the blocks and any issues, then click **Import**: the paper opens in the editor.

Already in a document? To bring in a piece of LaTeX, use **Paste LaTeX** from the command palette (the **?** button in the top bar).

## PDFs and scanned notes {#pdf}
- A `.pdf`, or a photo or scan of a page (`.jpg`, `.png`, up to 10 MB), handwritten maths notes included, is read into LaTeX by a maths OCR service, then reviewed like any import. Check the result against your page: handwriting is read well, but not perfectly.
- PDFs and scans use your plan's monthly **PDF pages** (10 a month on Free, 100 on Pro): each PDF page counts as one, and so does each image. Lilia checks them before reading the file, so a refused import costs nothing, and the pages don't count against the day's connector actions.
- From Claude or another assistant connected to Lilia, `import_document` takes the PDF or image, and `import_status` says when the document is ready.

## What this means {#beginner}
- Lilia turns your file into blocks (headings, paragraphs, equations, figures…) so you can edit it like any Lilia document.
- The review step shows what came across and what needs attention before anything is created.
- Your original file isn't changed.

## For LaTeX users {#expert}
- Unknown environments and commands are flagged in the review; most standard packages are recognised (see the coverage report).
- An Overleaf `.zip` is read as a project: its main `.tex`, its images and its `.bib`.
- Word headings, lists, tables, equations and captions map to blocks; styles that have no LaTeX meaning are dropped.
