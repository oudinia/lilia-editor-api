---
slug: connect-claude
lang: fr
title: Connecter votre IA à Lilia : Claude, ChatGPT, Cursor, VS Code et d’autres
summary: Permettre à votre application d’IA de lire vos documents, de vérifier qu’ils compilent et de faire des modifications visibles et annulables, grâce au connecteur de Lilia. Les étapes pour Claude, Claude Code, ChatGPT, Codex, Cursor, VS Code, Gemini CLI et d’autres.
skill: lilia-tutor
audience: all
tags: [claude, chatgpt, cursor, vscode, gemini, connecteur, mcp, applications d'ia]
keywords: claude claude-code chatgpt codex cursor vscode copilot gemini zed junie lm-studio mistral le-chat copilot-studio connecteur mcp connecter application intégration connecteur personnalisé assistant ia externe
---
# Connecter votre IA à Lilia

Le connecteur de Lilia permet à l’application d’IA que vous utilisez déjà de travailler dans vos documents Lilia : les lire, les écrire et les modifier, vérifier qu’ils compilent et les exporter. Vous gardez votre IA et son abonnement ; Lilia compte les actions sur votre offre.

## Étapes {#steps}
1. Dans votre application d’IA, ajoutez un **connecteur personnalisé** (certaines l’appellent serveur MCP) avec cette adresse : `https://editor.liliaeditor.com/api/mcp`. Les étapes de chaque application sont ci-dessous.
2. Quand l’application se connecte, Lilia s’ouvre : connectez-vous si on vous le demande, puis sur **Connecter … à Lilia ?** vérifiez ce qu’elle pourra faire et cliquez sur **Autoriser**.
3. De retour dans votre application, demandez avec vos mots : *« Liste mes documents Lilia »*, *« Mon article compile-t-il ? »*, *« Ajoute une phrase à la fin de l’introduction »*.
4. Avec le document ouvert dans Lilia, les modifications de l’application apparaissent aussitôt, avec **Annuler**. Pour revenir en arrière plus tard : **Historique**, puis restaurez la version enregistrée avant sa première modification.
5. Pour arrêter : retirez le connecteur dans votre application.

## Claude : web, ordinateur et téléphone {#claude}
1. Ouvrez **Customize → Connectors** (claude.ai/customize/connectors) et choisissez **+ Add → Add custom connector**.
2. Nom **Lilia**, adresse `https://editor.liliaeditor.com/api/mcp`, puis **Continue**. Gardez les réglages de connexion proposés, choisissez **Sign in now**, puis **Add**.
3. Dans une conversation, activez-le avec **+ → Connectors → Lilia**.
- L’offre gratuite accepte un connecteur personnalisé. En Team et Enterprise, un propriétaire l’ajoute dans **Organization settings → Connectors**, puis chaque membre se connecte.
- Un connecteur ajouté sur le web ou l’application de bureau apparaît aussi sur votre téléphone.

## Claude Code {#claude-code}
```
claude mcp add --transport http --scope user lilia https://editor.liliaeditor.com/api/mcp
```
Puis lancez `/mcp` dans Claude Code, choisissez **lilia** et connectez-vous dans le navigateur (ou `claude mcp login lilia`). `--scope user` le rend disponible dans tous vos projets.

## ChatGPT {#chatgpt}
1. Sur chatgpt.com, ouvrez **Plugins** et choisissez **+ → Add custom MCP server**.
2. Nom **lilia**, connexion **Public endpoint**, adresse `https://editor.liliaeditor.com/api/mcp`, authentification **OAuth**.
3. Confirmez l’avertissement, créez le plugin et installez-le. Dans une conversation, tapez **@** et choisissez **lilia**.
- Les offres ChatGPT qui peuvent ajouter un serveur personnalisé, et y écrire, dépendent de l’offre : voir l’aide d’OpenAI. ChatGPT vous demande de confirmer chaque modification.

## Codex (la ligne de commande d’OpenAI) {#codex}
```
codex mcp add lilia --url https://editor.liliaeditor.com/api/mcp
codex mcp login lilia
```
Ou ajoutez à `~/.codex/config.toml` :
```
[mcp_servers.lilia]
url = "https://editor.liliaeditor.com/api/mcp"
```

## Cursor {#cursor}
Ajoutez à `~/.cursor/mcp.json` (tous les projets) ou `.cursor/mcp.json` (un projet), puis connectez-vous quand Cursor le demande :
```
{ "mcpServers": { "lilia": { "url": "https://editor.liliaeditor.com/api/mcp" } } }
```

## VS Code avec GitHub Copilot {#vscode}
1. Palette de commandes → **MCP: Add Server** → **HTTP**.
2. Adresse `https://editor.liliaeditor.com/api/mcp`, nom **lilia**, puis choisissez où le garder (tous vos espaces de travail, ou celui-ci).
3. Connectez-vous dans le navigateur quand VS Code le demande (ou **Authenticate** dans les actions du serveur).

## Gemini CLI {#gemini}
Ajoutez à `~/.gemini/settings.json` (notez **httpUrl**) :
```
{ "mcpServers": { "lilia": { "httpUrl": "https://editor.liliaeditor.com/api/mcp" } } }
```
Puis lancez `/mcp auth lilia` et connectez-vous dans le navigateur.

## Zed, Junie, LM Studio, Mistral, Perplexity, Gemini, Copilot Studio {#more}
- **Zed** : dans les réglages, `"context_servers": { "lilia": { "url": "https://editor.liliaeditor.com/api/mcp" } }` ; Zed vous propose de vous connecter.
- **Junie** (JetBrains) : `~/.junie/mcp/mcp.json` avec `{ "mcpServers": { "lilia": { "url": "https://editor.liliaeditor.com/api/mcp" } } }`, puis `/mcp` → **lilia** → **Authorize**.
- **LM Studio** : **Program → Install → Edit mcp.json**, ajoutez la même entrée `mcpServers` ; LM Studio ouvre la page de connexion.
- **Mistral Le Chat** : un administrateur l’ajoute dans **Connecteurs → + Ajouter un connecteur → Connecteur MCP personnalisé**, nom **lilia**, l’adresse ci-dessus, puis **Connecter**.
- **Perplexity** : **Paramètres → Connecteurs → + Connecteur personnalisé**, l’adresse ci-dessus, authentification **OAuth**, puis connecter. Les connecteurs personnalisés sont réservés aux offres payantes (Pro, Max, Enterprise).
- **Application Gemini** : **Paramètres → Applications connectées → Ajouter une application personnalisée**, l’adresse ci-dessus, puis connectez-vous. Google ne le propose que pour certaines offres et certains pays ; si l’option n’apparaît pas, utilisez Gemini CLI.
- **Microsoft Copilot Studio** : **Tools → Add a tool → New tool → Model Context Protocol**, l’adresse ci-dessus, authentification **OAuth 2.0 → Dynamic discovery**.

## Toute autre application {#other}
Une application qui accepte les serveurs MCP distants avec OAuth se connecte avec la même adresse. Une qui n’exécute que des serveurs locaux peut passer par le pont `mcp-remote` (il faut Node.js) :
```
{ "mcpServers": { "lilia": { "command": "npx", "args": ["mcp-remote", "https://editor.liliaeditor.com/api/mcp", "--transport", "http-only"] } } }
```

## Des prompts prêts à l'emploi {#prompts}
Lilia propose à Claude des prompts tout prêts : dans Claude, cliquez sur **+** (ou tapez **/**) et choisissez un prompt Lilia, puis remplissez deux ou trois champs. Chacun fait le travail en un minimum d'étapes, ce qui économise vos actions du jour.
- **Rédiger un article** : un titre et le sujet ; Lilia crée le document avec un premier jet, le vérifie et vous donne le PDF.
- **Mettre au format d'une revue** : IEEE, ACM, Springer LNCS, Elsevier et d'autres.
- **Réparer ce qui ne compile pas**, **Ajouter des références**, **Exporter** (PDF, LaTeX, Overleaf, Word) et **Importer un fichier**.

## Ce que cela veut dire {#beginner}
- Claude agit **en votre nom** : il ne voit que les documents que vous pouvez ouvrir vous-même, et ne modifie que ceux dont vous êtes propriétaire ou que vous pouvez modifier. Un document partagé avec vous en lecture seule le reste.
- Il peut lire vos documents, vérifier qu'ils compilent, lire l'aide de Lilia et faire des modifications. Il ne peut ni partager ni supprimer un document.
- Chaque modification est visible dans votre éditeur et peut être annulée depuis l'Historique.
- Quand Lilia ajoute de nouvelles possibilités, déconnectez puis reconnectez Lilia pour que Claude les voie.

## Pour les utilisateurs de LaTeX {#expert}
- Le connecteur est un serveur Model Context Protocol (MCP) en HTTP (Streamable HTTP) ; la connexion passe par OAuth 2.1 via le fournisseur d'identité de Lilia (enregistrement dynamique ou document de métadonnées du client, PKCE). Le jeton d'une application ne sert qu'à ces outils, pas au reste de l'API de Lilia.
- `get_lilia_latex` renvoie le LaTeX que Lilia produit elle-même (préambule, paquets, correspondance des blocs) : c'est ce qui compile. `check_document` le compile sur le moment, sans enregistrer de validation.
- Les modifications passent par les blocs de Lilia : `add_block`, `edit_block`, `remove_block`, `reorder_blocks`, `set_title`, `set_document_settings`, `apply_lml` (remplace tout le corps par du LML), `paste_content` (colle du LaTeX, du Markdown ou du texte Lilia à l’endroit demandé et vérifie que tout compile : la façon de passer un article section par section) et `create_document`.
- Les fichiers passent par des liens, pas par la conversation : `export_document` renvoie un lien de téléchargement valable 15 minutes (pdf, tex, zip ou docx), et `start_import` renvoie une page où déposer le fichier à importer ; `import_status` en suit l'avancement.
