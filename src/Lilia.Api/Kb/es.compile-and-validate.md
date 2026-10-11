---
slug: compile-and-validate
lang: es
title: Comprueba que compila y corrige los errores
summary: Valida un documento, lee sus errores y avisos, corrígelos con Preguntar a Lilia y vuelve a comprobarlo.
skill: lilia-compile-doctor
audience: all
tags: [compilación, errores, validación, latex]
keywords: compilar error validar validación comprobar avisos corregir problemas latex dólar falta comando desconocido paquete llave modo matemático pdf
---
# Comprueba que compila y corrige los errores

## Pasos {#steps}
1. En el carril derecho del editor, abre **Validar** y haz clic en **Validar ahora** (o **Volver a validar** si ya se ejecutó).
   ![Validar ahora en el panel Validar](/help/img/compile-and-validate/es-1.png)
2. Lee el resultado: los **errores** impiden el PDF; los **avisos** dejan que se genere, pero conviene revisarlos. Cada uno indica el bloque del que viene cuando es posible.
3. Corregir un error: selecciona su bloque y pide en **Preguntar a Lilia** *«¿Por qué no compila? Corrige este error»*; aplica lo que proponga.
4. Haz clic en **Volver a validar** hasta que el panel diga que el documento compila sin errores.
   ![Una validación sin errores](/help/img/compile-and-validate/es-2.png)

En otros sitios: en **Documentos**, el filtro **Con problemas** muestra los documentos con errores o avisos, y cada tarjeta ofrece **Corregir**. En un teléfono: **⋯ → Revisar el documento** y luego **Pedir a Lilia que lo corrija** en un problema.

## Qué significa {#beginner}
- **Compilar** es convertir un artículo LaTeX en un PDF. Lilia lo hace por ti; *validar* es hacerlo a propósito para ver si algo falla.
- Un **error** impide generar el PDF; a menudo es un solo carácter que falta. Un **aviso** es una nota: el PDF se genera, pero algo puede verse mal (una referencia que falta, por ejemplo).
- Guardar no comprueba tu artículo. *Aún sin validar* solo significa que no se ha hecho ninguna comprobación.
- Los errores son normales y suelen ser pequeños. Corrige el primero y vuelve a comprobar: los siguientes suelen desaparecer con él.

## Para usuarios de LaTeX {#expert}
Causas frecuentes:
- `Missing $ inserted`: matemáticas (`_`, `^`) fuera del modo matemático. Rodéalas: `$x_i$`.
- `Undefined control sequence`: un comando que Lilia o LaTeX no conoce, o un paquete que falta (añádelo desde *LaTeX packages* en la paleta de comandos, tras el botón **?**).
- `Runaway argument` / `Missing } inserted`: una llave sin cerrar.

Corrige el **primer** error y vuelve a compilar antes de seguir; los siguientes suelen ser consecuencias. Para ver lo que Lilia genera de verdad, abre la vista LaTeX (vista dividida, o **Ver LaTeX** en un teléfono). La habilidad «doctor de compilación» (*«¿Por qué no compila?»*) lee el error y el bloque y propone el cambio más pequeño.
