---
slug: saving-and-versions
lang: en
title: Saving, versions and getting an earlier state back
summary: How Lilia saves as you type, what the save indicator tells you, how to keep a named version and how to restore one.
skill: lilia-tutor
audience: all
tags: [save, autosave, versions, history, restore, offline]
keywords: save saving autosave saved offline lost work version history restore undo earlier previous snapshot couldn't save retry
---
# Saving, versions and getting an earlier state back

## Steps {#steps}
1. Write: Lilia **saves as you type**. The indicator next to the title says *Saved*, *Saving…*, *Saved on this device* (offline) or *Couldn't save* (with **Retry**).
2. Keep a named version of this moment: hover the indicator and click **Save a version…** (or press ⌘S / Ctrl+S), give it a name if you like, and save.
   ![The save card with Save a version](/help/img/saving-and-versions/en-1.png)
3. See earlier versions: in the rail on the left, click the clock (**History**).
4. Get one back: pick it and click **Restore**. Lilia first saves the current state as a new version, so nothing is lost.

## What this means {#beginner}
- There is no Save button to forget: every change is saved within a moment.
- Offline? Keep writing. Your changes stay on this device and are sent when you're back online.
- A **version** is a snapshot you can return to: worth making before a big change or before sending the paper to someone.

## For LaTeX users {#expert}
- Versions are document-level snapshots (blocks and settings), not a diff per keystroke. Restoring creates a version of the current state first, so a restore is itself reversible.
- Ask Lilia's edits take a snapshot before writing, which is what **Undo AI changes** restores.
