---
slug: export
lang: fr
title: Télécharger votre article (PDF, LaTeX, Word…)
summary: Exporter un document en PDF, en fichier ou projet LaTeX façon Overleaf, en Word, Markdown, HTML ou au format de Lilia.
skill: lilia-tutor
audience: all
tags: [export, téléchargement, pdf, latex, word, overleaf]
keywords: exporter télécharger pdf latex tex zip overleaf word docx markdown html lilia lml imprimer soumettre revue envoyer fichier
---
# Télécharger votre article (PDF, LaTeX, Word…)

## Étapes {#steps}
1. Dans le document, cliquez sur **Exporter** (barre du haut).
2. Choisissez un format : **PDF**, **LaTeX**, **Markdown**, **HTML**, **Lilia (.lilia)** ou **Word (.docx)**.
   ![La fenêtre d’export](/help/img/export/fr-1.png)
3. Pour **LaTeX**, choisissez un **Fichier unique** (.tex) ou un **Projet multi-fichiers** (.zip), et sa disposition (à plat, ou **Style Overleaf**).
4. Lancez l’export : le fichier se télécharge.

Raccourci : la flèche à côté d’**Exporter** propose directement **Word (.docx)**, et **Tous les formats…**.

## Ce que cela veut dire {#beginner}
- Le **PDF** est ce que vous envoyez ou imprimez. **Word**, c’est pour quelqu’un qui doit modifier dans Word.
- **LaTeX** est la source que demandent la plupart des revues ; le projet style Overleaf s’ouvre tel quel dans Overleaf.
- Vérifiez d’abord le document (*Vérifier que le document compile*) : un PDF ne se fabrique qu’à partir d’un document sans erreur.

## Pour les utilisateurs de LaTeX {#expert}
- L’export LaTeX contient le préambule de Lilia, la bibliographie (`.bib`), les images, et `lilia-theme.sty` quand le document utilise un autre thème que Classic.
- Le PDF est compilé sur le serveur (pdfLaTeX, ou le moteur que demande la classe) ; le style du document s’applique.
- Lilia (.lilia / LML) est le format d’aller-retour sans perte : réimportez-le pour retrouver les mêmes blocs.
