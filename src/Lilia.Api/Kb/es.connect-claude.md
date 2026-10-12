---
slug: connect-claude
lang: es
title: Conectar Claude (u otra aplicación de IA) a Lilia
summary: Deja que Claude lea tus documentos, compruebe que compilan y haga cambios que ves y puedes deshacer, con el conector de Lilia.
skill: lilia-tutor
audience: all
tags: [claude, chatgpt, conector, mcp, aplicaciones de ia]
keywords: claude conector mcp chatgpt conectar aplicación integración conector personalizado claude.ai claude desktop asistente ia externo
---
# Conectar Claude (u otra aplicación de IA) a Lilia

## Pasos {#steps}
1. En Claude (claude.ai o la aplicación de escritorio), abre **Settings → Connectors** (Ajustes → Conectores) y elige **Add custom connector** (Añadir conector personalizado).
2. Llámalo **Lilia** y pon la dirección `https://editor.liliaeditor.com/api/mcp`. Deja vacíos los ajustes avanzados y añádelo.
3. Haz clic en **Connect** (Conectar). Se abre Lilia: inicia sesión si te lo pide y, en **¿Conectar Claude a Lilia?**, revisa lo que podrá hacer y haz clic en **Permitir**.
4. De vuelta en Claude, pide con tus palabras: *«Lista mis documentos de Lilia»*, *«¿Compila mi artículo?»*, *«Añade una frase al final de la introducción de Pythagore theorem»*.
5. Si el documento está abierto en Lilia, los cambios de Claude aparecen al instante, con **Claude ha cambiado este documento · Deshacer**. Si lo último que escribiste aún no está guardado, Lilia espera y ofrece **Cargar los cambios**.
6. Para volver atrás más tarde: **Historial** y restaura la versión **Before Claude edit** (antes del cambio de Claude), guardada antes de su primer cambio.
7. Para dejarlo: en Claude, **Settings → Connectors → Lilia**, y desconéctalo o elimínalo.

Otra aplicación que admita conectores MCP se conecta igual, con la misma dirección.

## Qué significa {#beginner}
- Claude actúa **en tu nombre**: solo ve los documentos que tú puedes abrir y solo cambia los que son tuyos o que puedes editar. Un documento compartido contigo como solo lectura sigue siendo de solo lectura.
- Puede leer tus documentos, comprobar si compilan, leer la ayuda de Lilia y hacer cambios. No puede compartir ni eliminar un documento.
- Cada cambio se ve en tu editor y se puede deshacer desde el Historial.
- Cuando Lilia añade nuevas posibilidades, desconecta y vuelve a conectar Lilia para que Claude las vea.

## Para usuarios de LaTeX {#expert}
- El conector es un servidor Model Context Protocol (MCP) por HTTP (Streamable HTTP); el inicio de sesión es OAuth 2.1 con el proveedor de identidad de Lilia (registro dinámico o documento de metadatos del cliente, PKCE). El token de una aplicación solo sirve para estas herramientas, no para el resto de la API de Lilia.
- `get_lilia_latex` devuelve el LaTeX que genera la propia Lilia (preámbulo, paquetes y correspondencia de bloques): es lo que compila. `check_document` lo compila en el momento, sin guardar una validación.
- Los cambios pasan por los bloques de Lilia: `add_block`, `edit_block`, `remove_block`, `reorder_blocks`, `set_title`, `set_document_settings`, `apply_lml` (sustituye todo el cuerpo por LML) y `create_document`.
