---
slug: export
lang: en
title: Download your paper (PDF, LaTeX, Word…)
summary: Export a document as a PDF, a LaTeX file or Overleaf-style project, Word, Markdown, HTML or Lilia's own format.
skill: lilia-tutor
audience: all
tags: [export, download, pdf, latex, word, overleaf]
keywords: export download pdf latex tex zip overleaf word docx markdown html lilia lml print submit journal send file
---
# Download your paper (PDF, LaTeX, Word…)

## Steps {#steps}
1. In the document, click **Export** (top bar).
2. Choose a format: **PDF**, **LaTeX**, **Markdown**, **HTML**, **Lilia (.lilia)** or **Word (.docx)**.
   ![The Export dialog](/help/img/export/en-1.png)
3. For **LaTeX**, choose a **Single file** (.tex) or a **Multi-file project** (.zip), and its layout (flat, or **Overleaf-style**).
4. Click export; the file downloads.

Shortcut: the arrow next to **Export** has **Word (.docx)** directly, and **All formats…**.

## What this means {#beginner}
- **PDF** is what you send or print. **Word** is for someone who needs to edit in Word.
- **LaTeX** is the source most journals ask for; the Overleaf-style project opens as-is in Overleaf.
- Check the document first (*Check that it compiles*): a PDF can only be made from a document without errors.

## For LaTeX users {#expert}
- The LaTeX export carries Lilia's preamble, the bibliography (`.bib`), images, and `lilia-theme.sty` when the document uses a theme other than Classic.
- The PDF is compiled on the server (pdfLaTeX, or the engine the class needs); the document's Look applies.
- Lilia (.lilia / LML) is the lossless round-trip format: import it back to get the same blocks.
