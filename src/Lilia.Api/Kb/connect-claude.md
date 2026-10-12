---
slug: connect-claude
lang: en
title: Connect Claude (or another AI app) to Lilia
summary: Let Claude read your documents, check they compile and make changes you can see and undo, through Lilia's connector.
skill: lilia-tutor
audience: all
tags: [claude, chatgpt, connector, mcp, ai apps]
keywords: claude connector mcp chatgpt connect app integration custom connector claude.ai claude desktop claude code outside app external ai assistant
---
# Connect Claude (or another AI app) to Lilia

## Steps {#steps}
1. In Claude (claude.ai or the desktop app), open **Settings → Connectors** and choose **Add custom connector**.
2. Name it **Lilia** and give the address `https://editor.liliaeditor.com/api/mcp`. Leave the advanced settings empty, then add it.
3. Click **Connect**. Lilia opens: sign in if asked, then on **Connect Claude to Lilia?** check what it may do and click **Allow**.
4. Back in Claude, ask in your own words: *"List my Lilia documents"*, *"Does my article compile?"*, *"Add a sentence at the end of the Introduction of Pythagore theorem"*.
5. With the document open in Lilia, Claude's changes appear at once, with **Claude changed this document · Undo**. If you were typing and it isn't saved yet, Lilia waits and offers **Load changes**.
6. To go back later: **History**, then restore the version **Before Claude edit**. It is saved before Claude's first change.
7. To stop: in Claude, **Settings → Connectors → Lilia**, then disconnect or remove it.

Another app that supports MCP connectors connects the same way, with the same address.

## What this means {#beginner}
- Claude works **as you**: it only sees documents you can open yourself, and it only changes documents you own or can edit. A document shared with you as view-only stays read-only.
- It can read your documents, check whether they compile, read Lilia's help and make changes. It cannot share or delete a document.
- Every change is visible in your editor and can be undone from History.
- When Lilia adds new abilities, disconnect and connect again so Claude sees them.

## For LaTeX users {#expert}
- The connector is a Model Context Protocol (MCP) server over Streamable HTTP; sign-in is OAuth 2.1 through Lilia's identity provider (dynamic client registration or client metadata documents, PKCE). An app's token works for these tools only, not for the rest of Lilia's API.
- `get_lilia_latex` returns the LaTeX Lilia itself emits (its preamble, packages and block mapping), which is what compiles; `check_document` compiles it now without saving a validation.
- Edits go through Lilia's blocks: `add_block`, `edit_block`, `remove_block`, `reorder_blocks`, `set_title`, `set_document_settings`, `apply_lml` (replaces the whole body with LML) and `create_document`.
