---
slug: blocks-and-lml
title: Blocks and LML
summary: How a Lilia document is built — typed blocks edited Word-style, with LML as their plain-text form you can paste in.
skill: lilia-tutor
audience: beginner
tags: [basics, lml, blocks, editor]
keywords: block lml paste paragraph heading equation figure table theorem abstract bibliography toc structure document format
---
# Blocks and LML

**The model.** A Lilia document is a sequence of **typed blocks** — `@heading`, `@paragraph`, `@equation`, `@figure`, `@table`, `@theorem`, `@code`, `@abstract`, `@list`, `@blockquote`, `@bibliography`, `@toc`. You edit them Word-style, but they compile to real LaTeX.

**LML** is the text form of those blocks: a `@blocktype[attrs]` line followed by 2-space-indented content. You can paste LML into Lilia and it parses into editable blocks — handy for moving content in quickly.

```lml
@heading[level=1]
  Introduction

@paragraph
  Deep networks have transformed the field. See Equation~(eq:loss).

@equation[mode=display, label="eq:loss"]
  \mathcal{L} = \mathcal{L}_{\text{CE}} + \lambda \lVert W \rVert_2^2
```

**Why blocks.** Each block knows its type, so Lilia can validate it, renumber cross-references, and emit correct LaTeX — you focus on content, not boilerplate.

**Next step.** Press `+` (or `/` in an empty paragraph) to insert a block by type; or paste LML directly. Related: cross-references, equations, the `lilia-tutor` skill.
