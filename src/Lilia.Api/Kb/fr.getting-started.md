---
slug: getting-started
lang: fr
title: Rédiger votre premier article
summary: D’une page blanche à un PDF : créer un document, écrire, ajouter une figure et une citation, le vérifier, le télécharger.
skill: lilia-tutor
audience: beginner
tags: [bases, prise en main, premier article]
keywords: commencer premier article nouveau document débuter écrire figure citation vérifier pdf télécharger débutant prise en main tutoriel
---
# Rédiger votre premier article

## Étapes {#steps}
1. Sur l’**Accueil**, cliquez sur **Nouvel article** (ou dans **Documents**, sur **Nouveau**). Choisissez **Document vierge**, donnez-lui un titre et créez-le.
   ![Le choix d’un nouveau document](/help/img/getting-started/fr-1.png)
2. Le document s’ouvre à côté de **Demander à Lilia**. Cliquez dans la page et écrivez. Chaque paragraphe, titre ou liste est un **bloc**. Sur une ligne vide, tapez **/** pour ajouter un titre, une équation, une figure ou un tableau.
   ![Le menu des blocs après /](/help/img/getting-started/fr-2.png)
3. Ajouter une figure : **/** → *Figure*, téléversez votre image, puis écrivez sa légende en dessous.
4. Citer un article : ouvrez **Demander à Lilia** (à droite) et demandez *« Cite un article à partir de son DOI ou de son identifiant arXiv »* avec le DOI. Lilia vous donne la référence et la citation à appliquer.
5. Le vérifier : dans le rail à gauche, cliquez sur le bouclier (**Valider**), puis sur **Valider maintenant**. S’il affiche des erreurs, voyez *Vérifier que le document compile et corriger les erreurs*.
   ![Le panneau Valider](/help/img/getting-started/fr-3.png)
6. Le télécharger : **Exporter** (barre du haut) → **PDF**.

Votre travail s’enregistre pendant que vous tapez. Pour garder une copie nommée de ce moment, survolez l’indicateur d’enregistrement et cliquez sur **Enregistrer une version** (⌘S / Ctrl+S).

## Ce que cela veut dire {#beginner}
- Lilia écrit le **LaTeX** pour vous : le langage que la plupart des revues et des universités utilisent pour mettre en page les articles. Vous écrivez comme dans un traitement de texte ; Lilia en fait un PDF correctement composé.
- Un **bloc** est un morceau de votre article : un paragraphe, un titre, une équation, une figure. Vous pouvez le déplacer, changer son type ou le supprimer à part.
- **Valider** veut dire : Lilia fabrique le PDF et vous signale ce qui l’en empêche. Les erreurs sont normales pendant l’écriture, et chacune indique le bloc à corriger.
- Bloqué ? Demandez à Lilia avec vos mots, en français, en anglais ou en espagnol.

## Pour les utilisateurs de LaTeX {#expert}
- Chaque bloc devient du LaTeX : titre → `\section`, équation → `equation`/`align`, figure → `figure` avec `\includegraphics` et `\caption`, citation → `\citep`/`\citet` (natbib + BibTeX).
- La classe du document et ses options sont dans la ligne sous le titre (*Type et classe*, *Options de classe*, *Mise en page*, *Style*). Lilia gère le préambule ; le bouton **?** de la barre du haut ouvre la palette de commandes (*LaTeX packages*, *Paste LaTeX*).
- **Paramètres → Préférences de l’éditeur → Édition LaTeX native** affiche les commandes et la source pendant la saisie. **Exporter → LaTeX** donne un `.tex` ou un projet `.zip` (à plat ou façon Overleaf).
