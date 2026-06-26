---
slug: latex-table
title: Data → LaTeX table
summary: Convert Excel / Sheets / CSV (or pasted tab-separated data) into a clean booktabs @table block.
tool: latex-table
skill: lilia-table
audience: all
tags: [tables, booktabs, csv, data]
keywords: table booktabs csv excel sheets tabular data column row caption label tab separated paste grid
---
# Data → LaTeX table

**What it does.** Paste tabular data — copied from Excel/Sheets (tab-separated), or CSV — and Lilia produces a publication-quality `@table` block using `booktabs` rules (no vertical lines, proper `\toprule`/`\midrule`/`\bottomrule`).

**When to use it.** You have rows and columns somewhere else and want them as a real LaTeX table without hand-aligning `&` and `\\`.

**How.**
1. Open the **LaTeX table** tool (or the table block in the editor).
2. Paste your data — the first row is treated as the header.
3. Set a caption and a `label` (e.g. `tab:results`), then copy or **Open in the editor**.

**What you get.**
```lml
@table[caption="Accuracy by method.", label="tab:acc"]
  | Method   | Accuracy |
  | Baseline | 0.71 |
  | Ours     | 0.86 |
```
which compiles to a `booktabs` `tabular`. Refer to it later with `Table~(tab:acc)`.

**Tips.** Keep data honest — the tool only formats the numbers you give it. For wide tables, consider `\small` or landscape; for decimals, align on the decimal point. Related: cross-references, the `lilia-table` skill.
