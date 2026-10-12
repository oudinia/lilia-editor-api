---
slug: document-look
lang: es
title: Elige el estilo de tu artículo
summary: Ajusta el tipo y la clase del documento, su diseño de página y su tema: Classic, Cerulean, Index, Carnet, Gazette o Exposition.
skill: lilia-tutor
audience: all
tags: [estilo, tema, diseño, clase, ajustes de página]
keywords: estilo tema aspecto diseño classic cerulean index carnet gazette exposition papel márgenes columnas tamaño de letra numeración encabezado pie de página clase artículo informe libro diapositivas
---
# Elige el estilo de tu artículo

## Pasos {#steps}
1. En el documento, haz clic en el botón de tipo de la barra superior (por ejemplo **Artículo ▾**): se abren sus ajustes.
   ![Los ajustes del documento, con el estilo](/help/img/document-look/es-1.png)
2. **Tipo y clase**: artículo, informe, libro, diapositivas…
3. **Opciones de clase**: tamaño del papel, tamaño de letra, columnas, una o dos caras.
4. **Diseño**: márgenes, interlineado, sangría de párrafo, numeración de páginas, encabezado y pie de página.
5. **Estilo**: el **Tema del documento**, el papel (*Papel del tema* o *Siempre blanco*), los colores de las secciones y el estilo de las tablas.

Los temas:
- **Classic**: envíos, y todo documento con una clase de editorial.
- **Cerulean**: informes, tesis, manuales de laboratorio.
- **Index**: apuntes, ciclos de clases, manuales.
- **Carnet**: ensayos, humanidades, lectura larga en pantalla.
- **Gazette**: boletines, carteles, divulgación (nunca un artículo).
- **Exposition**: diapositivas y carteles en pantalla (solo diapositivas).

## Qué significa {#beginner}
- El estilo es el aspecto del PDF: tipografías, colores, espacios. Tu texto no cambia.
- ¿Envías a una revista? Deja **Classic**: la revista aplica su propio estilo.

## Para usuarios de LaTeX {#expert}
- Tipo y clase fijan `\documentclass`; las opciones de clase son sus opciones. Un documento con tema carga `lilia-theme.sty` (o un tema beamer para diapositivas); la exportación LaTeX incluye el paquete.
- Con una clase de editorial (IEEEtran, acmart…), deja Classic: la clase de la revista fija el estilo.
