---
slug: saving-and-versions
lang: fr
title: Enregistrement, versions et retour à un état antérieur
summary: Comment Lilia enregistre pendant la saisie, ce que dit l’indicateur, comment garder une version nommée et la restaurer.
skill: lilia-tutor
audience: all
tags: [enregistrement, versions, historique, restaurer, hors ligne]
keywords: enregistrer enregistrement automatique enregistré hors ligne travail perdu version historique restaurer annuler état précédent instantané réessayer
---
# Enregistrement, versions et retour à un état antérieur

## Étapes {#steps}
1. Écrivez : Lilia **enregistre pendant la saisie**. L’indicateur à côté du titre affiche *Enregistré*, *Enregistrement…*, *Enregistré sur cet appareil* (hors ligne) ou *Enregistrement impossible* (avec **Réessayer**).
2. Garder une version nommée de ce moment : survolez l’indicateur et cliquez sur **Enregistrer une version…** (ou ⌘S / Ctrl+S), donnez-lui un nom si vous voulez, et enregistrez.
   ![La carte d’enregistrement avec Enregistrer une version](/help/img/saving-and-versions/fr-1.png)
3. Voir les versions précédentes : dans le rail à gauche, cliquez sur l’horloge (**Historique**).
4. En récupérer une : choisissez-la et cliquez sur **Restaurer**. Lilia enregistre d’abord l’état actuel comme nouvelle version : rien n’est perdu.

## Ce que cela veut dire {#beginner}
- Pas de bouton Enregistrer à oublier : chaque modification est enregistrée en un instant.
- Hors ligne ? Continuez à écrire. Vos modifications restent sur cet appareil et partent dès votre retour en ligne.
- Une **version** est un instantané auquel revenir : utile avant un gros changement ou avant d’envoyer l’article à quelqu’un.

## Pour les utilisateurs de LaTeX {#expert}
- Les versions sont des instantanés du document entier (blocs et réglages), pas un historique frappe par frappe. Restaurer crée d’abord une version de l’état actuel : une restauration est elle-même réversible.
- Les modifications de Demander à Lilia prennent un instantané avant d’écrire : c’est ce que restaure **Annuler les modifications de l’IA**.
