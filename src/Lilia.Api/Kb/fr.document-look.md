---
slug: document-look
lang: fr
title: Choisir le style de votre article
summary: Régler le type et la classe du document, sa mise en page et son thème : Classic, Cerulean, Index, Carnet, Gazette ou Exposition.
skill: lilia-tutor
audience: all
tags: [style, thème, mise en page, classe, réglages de page]
keywords: style thème apparence design classic cerulean index carnet gazette exposition papier mise en page marges colonnes taille du texte numérotation en-tête pied de page classe article rapport livre diapositives
---
# Choisir le style de votre article

## Étapes {#steps}
1. Dans le document, cliquez sur le bouton de type dans la barre du haut (par exemple **Article ▾**) : ses réglages s’ouvrent.
   ![Les réglages du document, avec le style](/help/img/document-look/fr-1.png)
2. **Type et classe** : article, rapport, livre, diapositives…
3. **Options de classe** : format du papier, taille du texte, colonnes, recto ou recto verso.
4. **Mise en page** : marges, interligne, retrait de paragraphe, numérotation des pages, en-tête et pied de page.
5. **Style** : le **Thème du document**, le papier (*Papier du thème* ou *Toujours blanc*), les couleurs des sections et le style des tableaux.

Les thèmes :
- **Classic** : soumissions, et tout document avec une classe d’éditeur.
- **Cerulean** : rapports, thèses, manuels de laboratoire.
- **Index** : notes de cours, séries de cours, manuels.
- **Carnet** : essais, sciences humaines, longue lecture à l’écran.
- **Gazette** : lettres d’information, affiches, médiation (jamais un article).
- **Exposition** : diapositives et affiches à l’écran (diapositives seulement).

## Ce que cela veut dire {#beginner}
- Le style, c’est l’allure du PDF : polices, couleurs, espacements. Votre texte ne change pas.
- Vous envoyez à une revue ? Gardez **Classic** : la revue applique son propre style.

## Pour les utilisateurs de LaTeX {#expert}
- Type et classe règlent `\documentclass` ; les options de classe sont ses options. Un document avec thème charge `lilia-theme.sty` (ou un thème beamer pour les diapositives) ; l’export LaTeX inclut le paquet.
- Avec une classe d’éditeur (IEEEtran, acmart…), gardez Classic : la classe de la revue fixe le style.
