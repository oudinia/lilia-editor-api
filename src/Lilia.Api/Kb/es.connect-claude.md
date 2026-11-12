---
slug: connect-claude
lang: es
title: Conecta tu IA a Lilia: Claude, ChatGPT, Cursor, VS Code y más
summary: Deja que tu aplicación de IA lea tus documentos, compruebe que compilan y haga cambios que ves y puedes deshacer, con el conector de Lilia. Pasos para Claude, Claude Code, ChatGPT, Codex, Cursor, VS Code, Gemini CLI y más.
skill: lilia-tutor
audience: all
tags: [claude, chatgpt, cursor, vscode, gemini, conector, mcp, aplicaciones de ia]
keywords: claude claude-code chatgpt codex cursor vscode copilot gemini zed junie lm-studio mistral le-chat copilot-studio conector mcp conectar aplicación integración conector personalizado asistente ia externo
---
# Conecta tu IA a Lilia

El conector de Lilia permite que la aplicación de IA que ya usas trabaje en tus documentos de Lilia: leerlos, escribirlos y editarlos, comprobar que compilan y exportarlos. Conservas tu IA y su suscripción; Lilia cuenta las acciones en tu plan.

## Pasos {#steps}
1. En tu aplicación de IA, añade un **conector personalizado** (algunas lo llaman servidor MCP) con esta dirección: `https://editor.liliaeditor.com/api/mcp`. Los pasos de cada aplicación están abajo.
2. Cuando la aplicación se conecta, se abre Lilia: inicia sesión si te lo pide y, en **¿Conectar … a Lilia?**, revisa lo que podrá hacer y pulsa **Permitir**.
3. De vuelta en tu aplicación, pide con tus palabras: *«Lista mis documentos de Lilia»*, *«¿Compila mi artículo?»*, *«Añade una frase al final de la introducción»*.
4. Con el documento abierto en Lilia, los cambios de la aplicación aparecen al instante, con **Deshacer**. Para volver atrás más tarde: **Historial** y restaura la versión guardada antes de su primer cambio.
5. Para dejarlo: quita el conector en tu aplicación.

## Claude: web, escritorio y móvil {#claude}
1. Abre **Customize → Connectors** (claude.ai/customize/connectors) y elige **+ Add → Add custom connector**.
2. Nombre **Lilia**, dirección `https://editor.liliaeditor.com/api/mcp` y **Continue**. Deja los ajustes de inicio de sesión propuestos, elige **Sign in now** y luego **Add**.
3. En un chat, actívalo con **+ → Connectors → Lilia**.
- El plan gratuito admite un conector personalizado. En Team y Enterprise lo añade un propietario en **Organization settings → Connectors** y después cada miembro se conecta.
- Un conector añadido en la web o en la aplicación de escritorio aparece también en tu móvil.

## Claude Code {#claude-code}
```
claude mcp add --transport http --scope user lilia https://editor.liliaeditor.com/api/mcp
```
Después ejecuta `/mcp` en Claude Code, elige **lilia** e inicia sesión en el navegador (o `claude mcp login lilia`). `--scope user` lo deja disponible en todos tus proyectos.

## ChatGPT {#chatgpt}
1. En chatgpt.com, abre **Plugins** y elige **+ → Add custom MCP server**.
2. Nombre **lilia**, conexión **Public endpoint**, dirección `https://editor.liliaeditor.com/api/mcp`, autenticación **OAuth**.
3. Confirma el aviso, crea el plugin e instálalo. En un chat, escribe **@** y elige **lilia**.
- Qué planes de ChatGPT pueden añadir un servidor personalizado, y escribir con él, depende del plan: consulta la ayuda de OpenAI. ChatGPT te pide confirmar cada cambio.

## Codex (la línea de comandos de OpenAI) {#codex}
```
codex mcp add lilia --url https://editor.liliaeditor.com/api/mcp
codex mcp login lilia
```
O añade a `~/.codex/config.toml`:
```
[mcp_servers.lilia]
url = "https://editor.liliaeditor.com/api/mcp"
```

## Cursor {#cursor}
Añade a `~/.cursor/mcp.json` (todos los proyectos) o `.cursor/mcp.json` (un proyecto) e inicia sesión cuando Cursor lo pida:
```
{ "mcpServers": { "lilia": { "url": "https://editor.liliaeditor.com/api/mcp" } } }
```

## VS Code con GitHub Copilot {#vscode}
1. Paleta de comandos → **MCP: Add Server** → **HTTP**.
2. Dirección `https://editor.liliaeditor.com/api/mcp`, nombre **lilia** y elige dónde guardarlo (todos tus espacios de trabajo o este).
3. Inicia sesión en el navegador cuando VS Code lo pida (o **Authenticate** en las acciones del servidor).

## Gemini CLI {#gemini}
Añade a `~/.gemini/settings.json` (fíjate en **httpUrl**):
```
{ "mcpServers": { "lilia": { "httpUrl": "https://editor.liliaeditor.com/api/mcp" } } }
```
Después ejecuta `/mcp auth lilia` e inicia sesión en el navegador.

## Zed, Junie, LM Studio, Mistral, Copilot Studio {#more}
- **Zed**: en los ajustes, `"context_servers": { "lilia": { "url": "https://editor.liliaeditor.com/api/mcp" } }`; Zed te propone iniciar sesión.
- **Junie** (JetBrains): `~/.junie/mcp/mcp.json` con `{ "mcpServers": { "lilia": { "url": "https://editor.liliaeditor.com/api/mcp" } } }` y luego `/mcp` → **lilia** → **Authorize**.
- **LM Studio**: **Program → Install → Edit mcp.json**, añade la misma entrada `mcpServers`; LM Studio abre la página de inicio de sesión.
- **Mistral Le Chat**: un administrador lo añade en **Conectores → + Añadir conector → Conector MCP personalizado**, nombre **lilia**, la dirección de arriba y **Conectar**.
- **Microsoft Copilot Studio**: **Tools → Add a tool → New tool → Model Context Protocol**, la dirección de arriba, autenticación **OAuth 2.0 → Dynamic discovery**.

## Cualquier otra aplicación {#other}
Una aplicación que admite servidores MCP remotos con OAuth se conecta con la misma dirección. Una que solo ejecuta servidores locales puede usar el puente `mcp-remote` (necesita Node.js):
```
{ "mcpServers": { "lilia": { "command": "npx", "args": ["mcp-remote", "https://editor.liliaeditor.com/api/mcp", "--transport", "http-only"] } } }
```

## Prompts listos para usar {#prompts}
Lilia ofrece a Claude prompts ya preparados: en Claude, haz clic en **+** (o escribe **/**) y elige un prompt de Lilia, luego rellena dos o tres campos. Cada uno hace el trabajo en el menor número de pasos, lo que ahorra tus acciones del día.
- **Escribir un artículo**: un título y el tema; Lilia crea el documento con un primer borrador, lo comprueba y te da el PDF.
- **Dar formato para una revista**: IEEE, ACM, Springer LNCS, Elsevier y otras.
- **Arreglar lo que no compila**, **Añadir referencias**, **Exportar** (PDF, LaTeX, Overleaf, Word) e **Importar un archivo**.

## Qué significa {#beginner}
- Claude actúa **en tu nombre**: solo ve los documentos que tú puedes abrir y solo cambia los que son tuyos o que puedes editar. Un documento compartido contigo como solo lectura sigue siendo de solo lectura.
- Puede leer tus documentos, comprobar si compilan, leer la ayuda de Lilia y hacer cambios. No puede compartir ni eliminar un documento.
- Cada cambio se ve en tu editor y se puede deshacer desde el Historial.
- Cuando Lilia añade nuevas posibilidades, desconecta y vuelve a conectar Lilia para que Claude las vea.

## Para usuarios de LaTeX {#expert}
- El conector es un servidor Model Context Protocol (MCP) por HTTP (Streamable HTTP); el inicio de sesión es OAuth 2.1 con el proveedor de identidad de Lilia (registro dinámico o documento de metadatos del cliente, PKCE). El token de una aplicación solo sirve para estas herramientas, no para el resto de la API de Lilia.
- `get_lilia_latex` devuelve el LaTeX que genera la propia Lilia (preámbulo, paquetes y correspondencia de bloques): es lo que compila. `check_document` lo compila en el momento, sin guardar una validación.
- Los cambios pasan por los bloques de Lilia: `add_block`, `edit_block`, `remove_block`, `reorder_blocks`, `set_title`, `set_document_settings`, `apply_lml` (sustituye todo el cuerpo por LML), `paste_content` (pega LaTeX, Markdown o texto Lilia donde se pida y comprueba que compila: la forma de pasar un artículo sección por sección) y `create_document`.
- Los archivos viajan como enlaces, no por la conversación: `export_document` devuelve un enlace de descarga válido 15 minutos (pdf, tex, zip o docx), y `start_import` devuelve una página donde soltar el archivo que se importa; `import_status` sigue su avance.
