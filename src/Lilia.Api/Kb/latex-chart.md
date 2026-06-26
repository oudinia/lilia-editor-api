---
slug: latex-chart
title: Data → chart (pgfplots)
summary: Turn x/y data into a vector pgfplots chart inside a @figure block — line, scatter, or bar.
tool: latex-chart
skill: lilia-tools
audience: all
tags: [charts, pgfplots, figures, plots, data]
keywords: chart plot pgfplots tikz figure line scatter bar graph axis xy data series vector
---
# Data → chart (pgfplots)

**What it does.** Paste x/y data (or two columns) and Lilia generates a `pgfplots` chart wrapped in a `@figure` block — a vector plot that compiles natively, not an image.

**When to use it.** You have numeric series and want a publication chart that scales crisply and matches the paper's fonts, without learning `pgfplots` syntax.

**How.**
1. Open the **chart** tool (or the chart/figure flow in the editor).
2. Paste your data and pick a type — line, scatter, or bar.
3. Set axis labels, a caption and a `label` (e.g. `fig:loss`), then copy or **Open in the editor**.

**What you get.** A `@figure` containing a `tikzpicture`/`axis` with your series, ready to cross-reference with `Figure~(fig:loss)`. Because it's vector LaTeX, it restyles with the document.

**Tips.** Keep series small and legible; for many points prefer a line over scatter. Real datasets only — the tool plots what you give it. Related: figures, cross-references, the `lilia-tools` skill.
