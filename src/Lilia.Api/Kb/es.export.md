---
slug: export
lang: es
title: Descarga tu artículo (PDF, LaTeX, Word…)
summary: Exporta un documento como PDF, archivo o proyecto LaTeX al estilo de Overleaf, Word, Markdown, HTML o el formato de Lilia.
skill: lilia-tutor
audience: all
tags: [exportar, descargar, pdf, latex, word, overleaf]
keywords: exportar descargar pdf latex tex zip overleaf word docx markdown html lilia lml imprimir enviar revista archivo
---
# Descarga tu artículo (PDF, LaTeX, Word…)

## Pasos {#steps}
1. En el documento, haz clic en **Exportar** (barra superior).
2. Elige un formato: **PDF**, **LaTeX**, **Markdown**, **HTML**, **Lilia (.lilia)** o **Word (.docx)**.
   ![La ventana de exportación](/help/img/export/es-1.png)
3. Para **LaTeX**, elige **Archivo único** (.tex) o **Proyecto multi-archivo** (.zip), y su disposición (plana o **Estilo Overleaf**).
4. Exporta: el archivo se descarga.

Atajo: la flecha junto a **Exportar** ofrece directamente **Word (.docx)** y **Todos los formatos…**.

## Qué significa {#beginner}
- El **PDF** es lo que envías o imprimes. **Word** es para quien necesita editar en Word.
- **LaTeX** es el código que piden la mayoría de revistas; el proyecto al estilo de Overleaf se abre tal cual en Overleaf.
- Revisa antes el documento (*Comprueba que compila*): solo se genera un PDF de un documento sin errores.

## Para usuarios de LaTeX {#expert}
- La exportación LaTeX incluye el preámbulo de Lilia, la bibliografía (`.bib`), las imágenes y `lilia-theme.sty` cuando el documento usa un tema distinto de Classic.
- El PDF se compila en el servidor (pdfLaTeX, o el motor que pida la clase); se aplica el estilo del documento.
- Lilia (.lilia / LML) es el formato de ida y vuelta sin pérdidas: impórtalo de nuevo para recuperar los mismos bloques.
