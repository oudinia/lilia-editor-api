---
slug: connect-claude
lang: en
title: Connect your AI to Lilia: Claude, ChatGPT, Cursor, VS Code and more
summary: Let your own AI app read your documents, check they compile and make changes you can see and undo, through Lilia's connector. Steps for Claude, Claude Code, ChatGPT, Codex, Cursor, VS Code, Gemini CLI and more.
skill: lilia-tutor
audience: all
tags: [claude, chatgpt, cursor, vscode, gemini, connector, mcp, ai apps]
keywords: claude claude-code chatgpt codex cursor vscode copilot gemini zed junie lm-studio mistral le-chat copilot-studio connector mcp chatgpt connect app integration custom connector claude.ai claude desktop claude code outside app external ai assistant
---
# Connect your AI to Lilia

Lilia's connector lets the AI app you already use work in your Lilia documents: read them, write and edit them, check that they compile and export them. You keep your own AI and its subscription; Lilia counts the actions on your plan.

## Steps {#steps}
1. In your AI app, add a **custom connector** (some apps call it an MCP server) with this address: `https://editor.liliaeditor.com/api/mcp`. The exact steps for each app are below.
2. When the app connects, Lilia opens: sign in if asked, then on **Connect … to Lilia?** check what it may do and click **Allow**.
3. Back in your app, ask in your own words: *"List my Lilia documents"*, *"Does my article compile?"*, *"Add a sentence at the end of the Introduction"*.
4. With the document open in Lilia, the app's changes appear at once, with **Undo**. To go back later: **History**, then restore the version saved before the app's first change.
5. To stop: remove the connector in your app.

## Claude: web, desktop and phone {#claude}
1. Open **Customize → Connectors** (claude.ai/customize/connectors) and choose **+ Add → Add custom connector**.
2. Name **Lilia**, address `https://editor.liliaeditor.com/api/mcp`, then **Continue**. Keep the suggested sign-in settings and choose **Sign in now**, then **Add**.
3. In a chat, turn it on with **+ → Connectors → Lilia**.
- Free plans hold one custom connector. On Team and Enterprise, an owner adds it under **Organization settings → Connectors**, then each member connects.
- A connector added on the web or the desktop app appears on your phone too.

## Claude Code {#claude-code}
```
claude mcp add --transport http --scope user lilia https://editor.liliaeditor.com/api/mcp
```
Then run `/mcp` in Claude Code, choose **lilia** and sign in in the browser (or `claude mcp login lilia`). `--scope user` makes it available in all your projects.

## ChatGPT {#chatgpt}
1. On chatgpt.com, open **Plugins** and choose **+ → Add custom MCP server**.
2. Name **lilia**, connection **Public endpoint**, address `https://editor.liliaeditor.com/api/mcp`, authentication **OAuth**.
3. Confirm the warning, create the plugin and install it. In a chat, type **@** and pick **lilia**.
- Which ChatGPT plans can add custom servers, and whether they may write, depends on the plan: see OpenAI's help. ChatGPT asks you to confirm each change before it is made.

## Codex (OpenAI's command line) {#codex}
```
codex mcp add lilia --url https://editor.liliaeditor.com/api/mcp
codex mcp login lilia
```
Or add to `~/.codex/config.toml`:
```
[mcp_servers.lilia]
url = "https://editor.liliaeditor.com/api/mcp"
```

## Cursor {#cursor}
Add to `~/.cursor/mcp.json` (all projects) or `.cursor/mcp.json` (one project), then sign in when Cursor asks:
```
{ "mcpServers": { "lilia": { "url": "https://editor.liliaeditor.com/api/mcp" } } }
```

## VS Code with GitHub Copilot {#vscode}
1. Command Palette → **MCP: Add Server** → **HTTP**.
2. Address `https://editor.liliaeditor.com/api/mcp`, server name **lilia**, then choose where to keep it (all your workspaces, or this one).
3. Sign in in the browser when VS Code asks (or use **Authenticate** in the server's actions).

## Gemini CLI {#gemini}
Add to `~/.gemini/settings.json` (note **httpUrl**):
```
{ "mcpServers": { "lilia": { "httpUrl": "https://editor.liliaeditor.com/api/mcp" } } }
```
Then run `/mcp auth lilia` and sign in in the browser.

## Zed, Junie, LM Studio, Mistral, Perplexity, Gemini, Copilot Studio {#more}
- **Zed**: in settings, `"context_servers": { "lilia": { "url": "https://editor.liliaeditor.com/api/mcp" } }`; Zed asks you to sign in.
- **Junie** (JetBrains): `~/.junie/mcp/mcp.json` with `{ "mcpServers": { "lilia": { "url": "https://editor.liliaeditor.com/api/mcp" } } }`, then `/mcp` → **lilia** → **Authorize**.
- **LM Studio**: **Program → Install → Edit mcp.json**, add the same `mcpServers` entry; LM Studio opens the sign-in page.
- **Mistral Le Chat**: an administrator adds it under **Connectors → + Add Connector → Custom MCP Connector**, name **lilia**, the address above, then **Connect**.
- **Perplexity**: **Settings → Connectors → + Custom connector**, the address above, authentication **OAuth**, then connect. Custom connectors are on paid plans (Pro, Max, Enterprise).
- **Gemini app**: **Settings → Connected apps → Add a custom app**, the address above, then sign in. Google offers it on some plans and countries only; if the option isn't there, use Gemini CLI.
- **Microsoft Copilot Studio**: **Tools → Add a tool → New tool → Model Context Protocol**, the address above, authentication **OAuth 2.0 → Dynamic discovery**.

## Any other app {#other}
An app that supports remote MCP servers with OAuth connects with the same address. One that only runs local servers can use the `mcp-remote` bridge (needs Node.js):
```
{ "mcpServers": { "lilia": { "command": "npx", "args": ["mcp-remote", "https://editor.liliaeditor.com/api/mcp", "--transport", "http-only"] } } }
```

## Ready-made prompts {#prompts}
Lilia gives your app ready-made prompts: in Claude, click **+** (or type **/**) and choose a Lilia prompt (other apps show them in their own prompt list), then fill in two or three fields. Each one does the job in the fewest steps, which saves your daily actions.
- **Write a paper**: a title and what it is about; Lilia creates the document with a first draft, checks it and gives you the PDF.
- **Format for a venue**: IEEE, ACM, Springer LNCS, Elsevier and others.
- **Fix what doesn't compile**, **Add references**, **Export** (PDF, LaTeX, Overleaf, Word) and **Import a file**.

## What this means {#beginner}
- Your app works **as you**: it only sees documents you can open yourself, and it only changes documents you own or can edit. A document shared with you as view-only stays read-only.
- It can read your documents, check whether they compile, read Lilia's help and make changes. It cannot share or delete a document.
- Every change is visible in your editor and can be undone from History.
- When Lilia adds new abilities, disconnect and connect again so your app sees them.

## For LaTeX users {#expert}
- The connector is a Model Context Protocol (MCP) server over Streamable HTTP; sign-in is OAuth 2.1 through Lilia's identity provider (dynamic client registration or client metadata documents, PKCE). An app's token works for these tools only, not for the rest of Lilia's API.
- `get_lilia_latex` returns the LaTeX Lilia itself emits (its preamble, packages and block mapping), which is what compiles; `check_document` compiles it now without saving a validation.
- Edits go through Lilia's blocks: `add_block`, `edit_block`, `remove_block`, `reorder_blocks`, `set_title`, `set_document_settings`, `apply_lml` (replaces the whole body with LML), `paste_content` (pastes LaTeX, Markdown or Lilia text where you ask and checks it compiles: the way to move a paper over section by section) and `create_document`.
- Files travel as links, not through the conversation: `export_document` returns a download link valid 15 minutes (pdf, tex, zip or docx), and `start_import` returns a page where you drop a file to import; `import_status` follows it.
