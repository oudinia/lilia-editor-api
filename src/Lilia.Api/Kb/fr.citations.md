---
slug: citations
lang: fr
title: Ajouter des références et les citer
summary: Construire la bibliographie de l’article à partir d’un DOI, d’un identifiant arXiv ou de BibTeX, et citer les entrées là où vous écrivez.
skill: lilia-citations
audience: all
tags: [citations, bibliographie, références, bibtex, doi]
keywords: citer citation référence références bibliographie bibtex doi arxiv isbn ajouter source article clé de citation natbib
---
# Ajouter des références et les citer

## Étapes {#steps}
1. Dans le rail à gauche, cliquez sur le livre (**Bibliographie**).
   ![Le panneau Bibliographie](/help/img/citations/fr-1.png)
2. Ajoutez une source : cherchez un article par son titre, saisissez un identifiant arXiv, utilisez **Ajouter une entrée** pour la remplir, ou **Importer BibTeX** pour importer un `.bib` entier.
3. Citez-la : placez le curseur à l’endroit voulu et cliquez sur **Citer** sur l’entrée. Ou tapez **@** dans votre texte et choisissez-la dans la liste.
4. Ou demandez à **Demander à Lilia** *« Cite un article à partir de son DOI ou de son identifiant arXiv »* avec l’identifiant.
5. La bibliographie s’imprime à la fin de l’article, dans le style de citation choisi dans le panneau.

## Ce que cela veut dire {#beginner}
- Une **référence** est une entrée de votre bibliographie (auteur, titre, année…). Une **citation** est l’endroit du texte qui y renvoie, par exemple « (Dupont, 2020) ».
- Vous ajoutez une référence une fois et la citez autant que nécessaire ; la liste finale se construit toute seule.

## Pour les utilisateurs de LaTeX {#expert}
- Les citations utilisent natbib avec BibTeX : **Citer** insère une citation entre parenthèses (`\citep{clé}` ; `\citet`, `\citeauthor` et `\citeyear` sont conservés à l’import) ; la clé est affichée sur chaque entrée et se modifie.
- **@** ouvre le sélecteur de références : les entrées de la bibliographie pour les citations, les blocs étiquetés pour les renvois `\cref`.
- Le `.bib` accompagne l’export LaTeX.
