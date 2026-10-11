---
slug: getting-started
lang: es
title: Escribe tu primer artículo
summary: De una página en blanco a un PDF: crea un documento, escribe, añade una figura y una cita, revísalo y descárgalo.
skill: lilia-tutor
audience: beginner
tags: [básico, primeros pasos, primer artículo]
keywords: empezar primer artículo nuevo documento comenzar escribir figura cita revisar pdf descargar principiante primeros pasos tutorial
---
# Escribe tu primer artículo

## Pasos {#steps}
1. En **Inicio**, haz clic en **Nuevo artículo** (o en **Documentos**, en **Nuevo**). Elige **Documento en blanco**, ponle un título y créalo.
   ![La elección de un documento nuevo](/help/img/getting-started/es-1.png)
2. Haz clic en la página y escribe. Cada párrafo, título o lista es un **bloque**. En una línea vacía, escribe **/** para añadir un título, una ecuación, una figura o una tabla.
   ![El menú de bloques tras /](/help/img/getting-started/es-2.png)
3. Añadir una figura: **/** → *Figura*, sube tu imagen y escribe su leyenda debajo.
4. Citar un artículo: abre **Preguntar a Lilia** (a la derecha) y pide *«Cita un artículo a partir de su DOI o su id de arXiv»* con el DOI. Lilia te da la referencia y la cita para aplicarlas.
5. Revisarlo: en el carril de la derecha, abre **Validar** y haz clic en **Validar ahora**. Si muestra errores, consulta *Comprueba que compila y corrige los errores*.
   ![El panel Validar](/help/img/getting-started/es-3.png)
6. Descargarlo: **Exportar** (barra superior) → **PDF**.

Tu trabajo se guarda mientras escribes. Para conservar una copia con nombre de este momento, pasa el ratón por el indicador de guardado y haz clic en **Guardar una versión** (⌘S / Ctrl+S).

## Qué significa {#beginner}
- Lilia escribe el **LaTeX** por ti: el lenguaje que usan la mayoría de revistas y universidades para maquetar artículos. Escribes como en un procesador de textos; Lilia lo convierte en un PDF bien compuesto.
- Un **bloque** es una pieza de tu artículo: un párrafo, un título, una ecuación, una figura. Puedes moverlo, cambiar su tipo o eliminarlo por separado.
- **Validar** significa: Lilia genera el PDF y te avisa de lo que lo impide. Los errores son normales mientras escribes, y cada uno señala el bloque que corregir.
- ¿Atascado? Pregunta a Lilia con tus palabras, en español, inglés o francés.

## Para usuarios de LaTeX {#expert}
- Cada bloque se convierte en LaTeX: título → `\section`, ecuación → `equation`/`align`, figura → `figure` con `\includegraphics` y `\caption`, cita → `\citep`/`\citet` (natbib + BibTeX).
- La clase del documento y sus opciones están en la línea bajo el título (*Tipo y clase*, *Opciones de clase*, *Diseño*, *Estilo*). Lilia gestiona el preámbulo; el botón **?** de la barra superior abre la paleta de comandos (*LaTeX packages*, *Paste LaTeX*).
- **Ajustes → Preferencias del editor → Edición LaTeX nativa** muestra los comandos y el código mientras escribes. **Exportar → LaTeX** da un `.tex` o un proyecto `.zip` (plano o al estilo de Overleaf).
