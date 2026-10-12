---
slug: connect-claude
lang: fr
title: Connecter Claude (ou une autre application d'IA) à Lilia
summary: Permettre à Claude de lire vos documents, de vérifier qu'ils compilent et de faire des modifications visibles et annulables, grâce au connecteur de Lilia.
skill: lilia-tutor
audience: all
tags: [claude, chatgpt, connecteur, mcp, applications d'ia]
keywords: claude connecteur mcp chatgpt connecter application intégration connecteur personnalisé claude.ai claude desktop assistant ia externe
---
# Connecter Claude (ou une autre application d'IA) à Lilia

## Étapes {#steps}
1. Dans Claude (claude.ai ou l'application de bureau), ouvrez **Settings → Connectors** (Paramètres → Connecteurs) et choisissez **Add custom connector** (Ajouter un connecteur personnalisé).
2. Nommez-le **Lilia** et donnez l'adresse `https://editor.liliaeditor.com/api/mcp`. Laissez les réglages avancés vides, puis ajoutez-le.
3. Cliquez sur **Connect** (Connecter). Lilia s'ouvre : connectez-vous si besoin, puis sur **Connecter Claude à Lilia ?** vérifiez ce qu'il pourra faire et cliquez sur **Autoriser**.
4. De retour dans Claude, demandez avec vos mots : *« Liste mes documents Lilia »*, *« Mon article compile-t-il ? »*, *« Ajoute une phrase à la fin de l'introduction de Pythagore theorem »*.
5. Si le document est ouvert dans Lilia, les modifications de Claude apparaissent aussitôt, avec **Claude a modifié ce document · Annuler**. Si votre dernière saisie n'est pas encore enregistrée, Lilia attend et propose **Charger les modifications**.
6. Pour revenir en arrière plus tard : **Historique**, puis restaurez la version **Before Claude edit** (avant la modification de Claude), enregistrée avant son premier changement.
7. Pour arrêter : dans Claude, **Settings → Connectors → Lilia**, puis déconnectez-le ou supprimez-le.

Une autre application qui accepte les connecteurs MCP se connecte de la même façon, avec la même adresse.

## Ce que cela veut dire {#beginner}
- Claude agit **en votre nom** : il ne voit que les documents que vous pouvez ouvrir vous-même, et ne modifie que ceux dont vous êtes propriétaire ou que vous pouvez modifier. Un document partagé avec vous en lecture seule le reste.
- Il peut lire vos documents, vérifier qu'ils compilent, lire l'aide de Lilia et faire des modifications. Il ne peut ni partager ni supprimer un document.
- Chaque modification est visible dans votre éditeur et peut être annulée depuis l'Historique.
- Quand Lilia ajoute de nouvelles possibilités, déconnectez puis reconnectez Lilia pour que Claude les voie.

## Pour les utilisateurs de LaTeX {#expert}
- Le connecteur est un serveur Model Context Protocol (MCP) en HTTP (Streamable HTTP) ; la connexion passe par OAuth 2.1 via le fournisseur d'identité de Lilia (enregistrement dynamique ou document de métadonnées du client, PKCE). Le jeton d'une application ne sert qu'à ces outils, pas au reste de l'API de Lilia.
- `get_lilia_latex` renvoie le LaTeX que Lilia produit elle-même (préambule, paquets, correspondance des blocs) : c'est ce qui compile. `check_document` le compile sur le moment, sans enregistrer de validation.
- Les modifications passent par les blocs de Lilia : `add_block`, `edit_block`, `remove_block`, `reorder_blocks`, `set_title`, `set_document_settings`, `apply_lml` (remplace tout le corps par du LML) et `create_document`.
- Les fichiers passent par des liens, pas par la conversation : `export_document` renvoie un lien de téléchargement valable 15 minutes (pdf, tex, zip ou docx), et `start_import` renvoie une page où déposer le fichier à importer ; `import_status` en suit l'avancement.
