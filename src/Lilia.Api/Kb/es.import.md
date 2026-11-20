---
slug: import
lang: es
title: Importa un artículo (.tex, Word, PDF, escaneos)
summary: Importa un archivo LaTeX o un proyecto de Overleaf, un documento de Word, un ePub, un PDF o un escaneo de apuntes a mano, revísalo y conviértelo en un documento de Lilia.
skill: lilia-tools
audience: all
tags: [importar, latex, word, docx, epub, overleaf, pdf, escaneo, manuscrito]
keywords: importar subir abrir tex latex zip overleaf word docx epub pdf escaneo foto imagen jpg png apuntes a mano ocr convertir revisar pegar artículo existente
---
# Importa un artículo (.tex, Word, PDF, escaneos)

## Pasos {#steps}
1. Abre **Importar / Exportar** (Inicio o el menú de Lilia) y quédate en la pestaña de importación.
2. Suelta tus archivos en el recuadro o haz clic en él para buscarlos: `.tex`, `.zip` (Overleaf), `.docx`, `.epub`, `.lilia`, `.lml`, `.md`, `.pdf`, y escaneos o fotos de una página en `.jpg` o `.png`.
   ![El recuadro de importación](/help/img/import/es-1.png)
3. Deja marcado **Revisar antes de importar** para comprobar el resultado primero y haz clic en **Importar**.
4. En la página de revisión, repasa los bloques y los posibles problemas y haz clic en **Importar**: el artículo se abre en el editor.

¿Ya estás en un documento? Para añadir un fragmento de LaTeX, usa **Paste LaTeX** en la paleta de comandos (el botón **?** de la barra superior).

## PDF y apuntes escaneados {#pdf}
- Un `.pdf`, o la foto o el escaneo de una página (`.jpg`, `.png`, hasta 10 MB), apuntes de matemáticas a mano incluidos, se lee en LaTeX con un servicio de OCR matemático y luego se revisa como cualquier importación. Compara el resultado con tu página: la escritura a mano se lee bien, pero no a la perfección.
- Los PDF y los escaneos usan las **páginas PDF** mensuales de tu plan: cada página de PDF cuenta como una, y cada imagen también. Tu plan muestra cuántas quedan. Un plan sin páginas PDF lo indica al importar.
- Desde Claude u otro asistente conectado a Lilia, `import_document` recibe el PDF o la imagen, e `import_status` indica cuándo está listo el documento.

## Qué significa {#beginner}
- Lilia convierte tu archivo en bloques (títulos, párrafos, ecuaciones, figuras…) que editas como cualquier documento de Lilia.
- La revisión muestra qué ha pasado y qué necesita tu atención antes de crear nada.
- Tu archivo original no cambia.

## Para usuarios de LaTeX {#expert}
- Los entornos y comandos desconocidos se señalan en la revisión; se reconocen la mayoría de los paquetes habituales (consulta el informe de cobertura).
- Un `.zip` de Overleaf se lee como proyecto: su `.tex` principal, sus imágenes y su `.bib`.
- Los títulos, listas, tablas, ecuaciones y leyendas de Word se convierten en bloques; los estilos sin equivalente en LaTeX se descartan.
