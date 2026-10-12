---
slug: import
lang: es
title: Importa un artículo (.tex, Word, ePub)
summary: Importa un archivo LaTeX o un proyecto de Overleaf, un documento de Word o un ePub, revísalo y conviértelo en un documento de Lilia.
skill: lilia-tools
audience: all
tags: [importar, latex, word, docx, epub, overleaf]
keywords: importar subir abrir tex latex zip overleaf word docx epub convertir revisar pegar artículo existente
---
# Importa un artículo (.tex, Word, ePub)

## Pasos {#steps}
1. Abre **Importar / Exportar** (Inicio o el menú de Lilia) y quédate en la pestaña de importación.
2. Suelta tus archivos en el recuadro o haz clic en él para buscarlos: `.tex`, `.zip` (Overleaf), `.docx`, `.epub`, `.lilia`, `.lml`.
   ![El recuadro de importación](/help/img/import/es-1.png)
3. Deja marcado **Revisar antes de importar** para comprobar el resultado primero y haz clic en **Importar**.
4. En la página de revisión, repasa los bloques y los posibles problemas y haz clic en **Importar**: el artículo se abre en el editor.

¿Ya estás en un documento? Para añadir un fragmento de LaTeX, usa **Paste LaTeX** en la paleta de comandos (el botón **?** de la barra superior).

## Qué significa {#beginner}
- Lilia convierte tu archivo en bloques (títulos, párrafos, ecuaciones, figuras…) que editas como cualquier documento de Lilia.
- La revisión muestra qué ha pasado y qué necesita tu atención antes de crear nada.
- Tu archivo original no cambia.

## Para usuarios de LaTeX {#expert}
- Los entornos y comandos desconocidos se señalan en la revisión; se reconocen la mayoría de los paquetes habituales (consulta el informe de cobertura).
- Un `.zip` de Overleaf se lee como proyecto: su `.tex` principal, sus imágenes y su `.bib`.
- Los títulos, listas, tablas, ecuaciones y leyendas de Word se convierten en bloques; los estilos sin equivalente en LaTeX se descartan.
