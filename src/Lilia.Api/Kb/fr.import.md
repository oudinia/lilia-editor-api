---
slug: import
lang: fr
title: Importer un article (.tex, Word, PDF, scans)
summary: Importer un fichier LaTeX ou un projet Overleaf, un document Word, un ePub, un PDF ou un scan de notes manuscrites, le relire, et en faire un document Lilia.
skill: lilia-tools
audience: all
tags: [import, latex, word, docx, epub, overleaf, pdf, scan, manuscrit]
keywords: importer téléverser ouvrir tex latex zip overleaf word docx epub pdf scan photo image jpg png notes manuscrites ocr convertir relire coller article existant
---
# Importer un article (.tex, Word, PDF, scans)

## Étapes {#steps}
1. Ouvrez **Import / Export** (Accueil, ou le menu Lilia) et restez sur l’onglet d’import.
2. Déposez vos fichiers dans la zone, ou cliquez dessus pour parcourir : `.tex`, `.zip` (Overleaf), `.docx`, `.epub`, `.lilia`, `.lml`, `.md`, `.pdf`, et les scans ou photos d’une page en `.jpg` ou `.png`.
   ![La zone d’import](/help/img/import/fr-1.png)
3. Laissez **Relire avant d’importer** coché pour vérifier le résultat d’abord, puis cliquez sur **Importer**.
4. Sur la page de relecture, parcourez les blocs et les éventuels problèmes, puis cliquez sur **Importer** : l’article s’ouvre dans l’éditeur.

Déjà dans un document ? Pour y ajouter un morceau de LaTeX, utilisez **Paste LaTeX** dans la palette de commandes (le bouton **?** de la barre du haut).

## PDF et notes scannées {#pdf}
- Un `.pdf`, ou la photo ou le scan d’une page (`.jpg`, `.png`, jusqu’à 10 Mo), notes de maths manuscrites comprises, est lu en LaTeX par un service d’OCR mathématique, puis relu comme tout import. Comparez le résultat à votre page : l’écriture manuscrite est bien lue, mais pas parfaitement.
- Les PDF et les scans utilisent les **pages PDF** mensuelles de votre offre (10 par mois sur Free, 100 sur Pro) : chaque page de PDF compte pour une, chaque image aussi. Lilia les vérifie avant de lire le fichier : un import refusé ne coûte rien, et les pages ne comptent pas dans les actions du connecteur de la journée.
- Depuis Claude ou un autre assistant connecté à Lilia, `import_document` prend le PDF ou l’image, et `import_status` indique quand le document est prêt.

## Ce que cela veut dire {#beginner}
- Lilia transforme votre fichier en blocs (titres, paragraphes, équations, figures…) que vous modifiez comme n’importe quel document Lilia.
- L’étape de relecture montre ce qui est passé et ce qui demande votre attention, avant que rien ne soit créé.
- Votre fichier d’origine n’est pas modifié.

## Pour les utilisateurs de LaTeX {#expert}
- Les environnements et commandes inconnus sont signalés à la relecture ; la plupart des paquets courants sont reconnus (voir le rapport de couverture).
- Un `.zip` Overleaf est lu comme un projet : son `.tex` principal, ses images et son `.bib`.
- Les titres, listes, tableaux, équations et légendes de Word deviennent des blocs ; les styles sans équivalent LaTeX sont ignorés.
