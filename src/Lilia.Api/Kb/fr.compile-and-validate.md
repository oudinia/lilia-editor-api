---
slug: compile-and-validate
lang: fr
title: Vérifier que le document compile et corriger les erreurs
summary: Valider un document, lire ses erreurs et avertissements, les corriger avec Demander à Lilia, puis vérifier à nouveau.
skill: lilia-compile-doctor
audience: all
tags: [compilation, erreurs, validation, latex]
keywords: compiler erreur valider validation vérifier avertissements corriger problèmes latex dollar manquant commande inconnue paquet accolade mode mathématique pdf
---
# Vérifier que le document compile et corriger les erreurs

## Étapes {#steps}
1. Dans le rail de droite de l’éditeur, ouvrez **Valider** et cliquez sur **Valider maintenant** (ou **Relancer la validation** si elle a déjà tourné).
   ![Valider maintenant dans le panneau Valider](/help/img/compile-and-validate/fr-1.png)
2. Lisez le résultat : les **erreurs** empêchent le PDF ; les **avertissements** le laissent se construire mais méritent un coup d’œil. Chacun indique le bloc d’où il vient quand c’est possible.
3. Corriger une erreur : sélectionnez son bloc, puis demandez à **Demander à Lilia** *« Pourquoi ça ne compile pas ? Corrige cette erreur »* et appliquez ce qu’il propose.
4. Cliquez sur **Relancer la validation** jusqu’à ce que le panneau indique que le document compile sans erreur.
   ![Une validation sans erreur](/help/img/compile-and-validate/fr-2.png)

Ailleurs : dans **Documents**, le filtre **Avec problèmes** liste les documents qui ont des erreurs ou des avertissements, et chaque carte propose **Corriger**. Sur un téléphone : **⋯ → Vérifier le document**, puis **Demander à Lilia de corriger** sur un problème.

## Ce que cela veut dire {#beginner}
- **Compiler**, c’est transformer un article LaTeX en PDF. Lilia le fait pour vous ; *valider*, c’est le faire exprès pour voir si quelque chose coince.
- Une **erreur** empêche de fabriquer le PDF ; c’est souvent un seul caractère manquant. Un **avertissement** est une remarque : le PDF se fabrique, mais quelque chose peut sembler faux (une référence manquante, par exemple).
- Enregistrer ne vérifie pas votre article. *Pas encore validé* veut seulement dire qu’aucune vérification n’a tourné.
- Les erreurs sont normales et souvent petites. Corrigez la première et vérifiez à nouveau : les suivantes disparaissent souvent avec elle.

## Pour les utilisateurs de LaTeX {#expert}
Causes fréquentes :
- `Missing $ inserted` : des maths (`_`, `^`) hors du mode mathématique. Entourez-les : `$x_i$`.
- `Undefined control sequence` : une commande inconnue de Lilia ou de LaTeX, ou un paquet manquant (ajoutez-le depuis *LaTeX packages* dans la palette de commandes, derrière le bouton **?**).
- `Runaway argument` / `Missing } inserted` : une accolade non fermée.

Corrigez la **première** erreur et recompilez avant de traiter les autres ; les suivantes sont souvent des cascades. Pour voir ce que Lilia produit réellement, ouvrez la vue LaTeX (vue partagée, ou **Voir le LaTeX** sur un téléphone). La compétence « docteur de compilation » (*« Pourquoi ça ne compile pas ? »*) lit l’erreur et le bloc et propose la plus petite correction.
