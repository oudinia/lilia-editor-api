---
slug: theorems
title: Theorems and proofs
summary: State results with @theorem (amsthm) and follow with a proof — numbered and cross-referenceable.
skill: lilia-tutor
audience: intermediate
tags: [math, theorems, amsthm, proofs]
keywords: theorem lemma proposition corollary definition proof amsthm qed statement numbered math
---
# Theorems and proofs

**The model.** Use a `@theorem` block for the statement and a `@theorem[proof]` block right after for the proof. Lilia uses `amsthm`, so results are numbered and cross-referenceable like any other block.

**Kinds.** `@theorem[theorem|lemma|proposition|corollary|definition|remark]` selects the environment; `@theorem[proof]` renders the proof with its QED box.

**Example.**
```lml
@theorem[theorem, label="thm:converge"]
  If $f$ is convex and $L$-smooth, gradient descent with step $\eta \le 1/L$ converges.

@theorem[proof]
  By $L$-smoothness, $f(x_{k+1}) \le f(x_k) - \tfrac{\eta}{2}\lVert \nabla f(x_k)\rVert^2$. Summing gives the bound. \qed
```
Refer back with `Theorem~(thm:converge)`.

**Tips.** Give every statement a `thm:`/`lem:` label so you can cite it. Keep the statement self-contained; push derivations into the proof. Related: cross-references, equations.
