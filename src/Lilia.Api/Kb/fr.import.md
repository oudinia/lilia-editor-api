---
slug: import
lang: fr
title: Importer un article (.tex, Word, ePub)
summary: Importer un fichier LaTeX ou un projet Overleaf, un document Word ou un ePub, le relire, et en faire un document Lilia.
skill: lilia-tools
audience: all
tags: [import, latex, word, docx, epub, overleaf]
keywords: importer téléverser ouvrir tex latex zip overleaf word docx epub convertir relire coller article existant
---
# Importer un article (.tex, Word, ePub)

## Étapes {#steps}
1. Ouvrez **Import / Export** (Accueil, ou le menu Lilia) et restez sur l’onglet d’import.
2. Déposez vos fichiers dans la zone, ou cliquez dessus pour parcourir : `.tex`, `.zip` (Overleaf), `.docx`, `.epub`, `.lilia`, `.lml`.
   ![La zone d’import](/help/img/import/fr-1.png)
3. Laissez **Relire avant d’importer** coché pour vérifier le résultat d’abord, puis cliquez sur **Importer**.
4. Sur la page de relecture, parcourez les blocs et les éventuels problèmes, puis cliquez sur **Importer** : l’article s’ouvre dans l’éditeur.

Déjà dans un document ? Pour y ajouter un morceau de LaTeX, utilisez **Paste LaTeX** dans la palette de commandes (le bouton **?** de la barre du haut).

## Ce que cela veut dire {#beginner}
- Lilia transforme votre fichier en blocs (titres, paragraphes, équations, figures…) que vous modifiez comme n’importe quel document Lilia.
- L’étape de relecture montre ce qui est passé et ce qui demande votre attention, avant que rien ne soit créé.
- Votre fichier d’origine n’est pas modifié.

## Pour les utilisateurs de LaTeX {#expert}
- Les environnements et commandes inconnus sont signalés à la relecture ; la plupart des paquets courants sont reconnus (voir le rapport de couverture).
- Un `.zip` Overleaf est lu comme un projet : son `.tex` principal, ses images et son `.bib`.
- Les titres, listes, tableaux, équations et légendes de Word deviennent des blocs ; les styles sans équivalent LaTeX sont ignorés.
