---
slug: citations
lang: es
title: Añade referencias y cítalas
summary: Crea la bibliografía del artículo desde un DOI, un id de arXiv o BibTeX, y cita las entradas donde escribes.
skill: lilia-citations
audience: all
tags: [citas, bibliografía, referencias, bibtex, doi]
keywords: citar cita referencia referencias bibliografía bibtex doi arxiv isbn añadir fuente artículo clave de cita natbib
---
# Añade referencias y cítalas

## Pasos {#steps}
1. En el carril de la izquierda, haz clic en el libro (**Bibliografía**).
   ![El panel Bibliografía](/help/img/citations/es-1.png)
2. Añade una fuente: busca un artículo por título, escribe un id de arXiv, usa **Agregar entrada** para rellenarla o **Importar BibTeX** para traer un `.bib` completo.
3. Cítala: coloca el cursor donde va la cita y haz clic en **Citar** en la entrada. O escribe **@** en tu texto y elígela de la lista.
4. O pide en **Preguntar a Lilia** *«Cita un artículo a partir de su DOI o su id de arXiv»* con el identificador.
5. La bibliografía se imprime al final del artículo, con el estilo de cita que elijas en el panel.

## Qué significa {#beginner}
- Una **referencia** es una entrada de tu bibliografía (autor, título, año…). Una **cita** es el lugar del texto que remite a ella, por ejemplo «(García, 2020)».
- Añades una referencia una vez y la citas tantas veces como necesites; la lista final se crea sola.

## Para usuarios de LaTeX {#expert}
- Las citas usan natbib con BibTeX: **Citar** inserta una cita entre paréntesis (`\citep{clave}`; `\citet`, `\citeauthor` y `\citeyear` se conservan al importar); la clave aparece en cada entrada y se puede editar.
- **@** abre el selector de referencias: las entradas de la bibliografía para citas y los bloques con etiqueta para referencias cruzadas `\cref`.
- El `.bib` acompaña a la exportación LaTeX.
