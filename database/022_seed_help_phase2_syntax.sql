-- Phase 2: Syntax & Feature Guides
-- Enriches existing syntax articles and adds new feature guides

-- ============================================================
-- SYNTAX: Inline Math (replace existing with richer content)
-- ============================================================
DELETE FROM blocks WHERE document_id = 'a0000000-0000-0000-0000-000000000010';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b2010000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000010', 'paragraph', '{"text": "Inline math lives **inside a paragraph**. Wrap any LaTeX expression in dollar signs and it renders as formatted math within your text."}', 0, 0),
('b2010000-0000-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000010', 'heading', '{"text": "Basic Syntax", "level": 2}', 1, 0),
('b2010000-0000-0000-0000-000000000003', 'a0000000-0000-0000-0000-000000000010', 'table', '{"headers": ["You Type", "You See", "LaTeX Output"], "rows": [["$E = mc^2$", "E = mc\u00b2", "$E = mc^2$"], ["$\\\\frac{a}{b}$", "a/b (fraction)", "\\\\frac{a}{b}"], ["$\\\\sqrt{x}$", "\u221ax", "\\\\sqrt{x}"], ["$\\\\sqrt[3]{x}$", "\u00b3\u221ax", "\\\\sqrt[3]{x}"], ["$x_i$", "x with subscript i", "x_i"], ["$x^{n+1}$", "x to the n+1", "x^{n+1}"], ["$\\\\alpha, \\\\beta, \\\\gamma, \\\\theta$", "\u03b1, \u03b2, \u03b3, \u03b8", "\\\\alpha, \\\\beta, \\\\gamma, \\\\theta"], ["$\\\\lambda, \\\\sigma, \\\\pi, \\\\omega$", "\u03bb, \u03c3, \u03c0, \u03c9", "\\\\lambda, \\\\sigma, \\\\pi, \\\\omega"]], "caption": "Basic inline math expressions"}', 2, 0),
('b2010000-0000-0000-0000-000000000004', 'a0000000-0000-0000-0000-000000000010', 'heading', '{"text": "Operators & Relations", "level": 2}', 3, 0),
('b2010000-0000-0000-0000-000000000005', 'a0000000-0000-0000-0000-000000000010', 'table', '{"headers": ["You Type", "Description", "LaTeX Output"], "rows": [["$\\\\sum_{i=1}^{n} x_i$", "Summation", "\\\\sum_{i=1}^{n} x_i"], ["$\\\\prod_{i=1}^{n} x_i$", "Product", "\\\\prod_{i=1}^{n} x_i"], ["$\\\\int_a^b f(x)\\\\,dx$", "Integral", "\\\\int_a^b f(x)\\\\,dx"], ["$\\\\lim_{x \\\\to \\\\infty}$", "Limit", "\\\\lim_{x \\\\to \\\\infty}"], ["$\\\\frac{\\\\partial f}{\\\\partial x}$", "Partial derivative", "\\\\frac{\\\\partial f}{\\\\partial x}"], ["$\\\\nabla f$", "Gradient", "\\\\nabla f"], ["$\\\\|x\\\\|$", "Norm", "\\\\|x\\\\|"], ["$\\\\lfloor x \\\\rfloor$", "Floor", "\\\\lfloor x \\\\rfloor"], ["$\\\\lceil x \\\\rceil$", "Ceiling", "\\\\lceil x \\\\rceil"]], "caption": "Operators and relations"}', 4, 0),
('b2010000-0000-0000-0000-000000000006', 'a0000000-0000-0000-0000-000000000010', 'heading', '{"text": "Styling & Sets", "level": 2}', 5, 0),
('b2010000-0000-0000-0000-000000000007', 'a0000000-0000-0000-0000-000000000010', 'table', '{"headers": ["You Type", "Use For", "LaTeX Output"], "rows": [["$\\\\mathbf{x}$", "Bold vector", "\\\\mathbf{x}"], ["$\\\\boldsymbol{\\\\theta}$", "Bold Greek", "\\\\boldsymbol{\\\\theta}"], ["$\\\\mathcal{O}(n)$", "Big-O notation", "\\\\mathcal{O}(n)"], ["$\\\\mathbb{R}$", "Real numbers", "\\\\mathbb{R}"], ["$\\\\mathbb{Z}, \\\\mathbb{N}, \\\\mathbb{C}$", "Integers, naturals, complex", "\\\\mathbb{Z}, \\\\mathbb{N}, \\\\mathbb{C}"], ["$\\\\hat{x}, \\\\bar{x}, \\\\tilde{x}$", "Decorations", "\\\\hat{x}, \\\\bar{x}, \\\\tilde{x}"], ["$\\\\dot{x}, \\\\ddot{x}$", "Time derivatives", "\\\\dot{x}, \\\\ddot{x}"], ["$\\\\text{if } x > 0$", "Text in math", "\\\\text{if } x > 0"]], "caption": "Math styling and set notation"}', 6, 0),
('b2010000-0000-0000-0000-000000000008', 'a0000000-0000-0000-0000-000000000010', 'heading', '{"text": "Examples in Context", "level": 2}', 7, 0),
('b2010000-0000-0000-0000-000000000009', 'a0000000-0000-0000-0000-000000000010', 'paragraph', '{"text": "The energy-mass equivalence $E = mc^2$ was proposed by Einstein in 1905."}', 8, 0),
('b2010000-0000-0000-0000-000000000010', 'a0000000-0000-0000-0000-000000000010', 'paragraph', '{"text": "Let $f: \\\\mathbb{R} \\\\to \\\\mathbb{R}$ be a continuous function on $[a, b]$."}', 9, 0),
('b2010000-0000-0000-0000-000000000011', 'a0000000-0000-0000-0000-000000000010', 'paragraph', '{"text": "The sample mean $\\\\bar{x} = \\\\frac{1}{n}\\\\sum_{i=1}^{n} x_i$ converges to $\\\\mu$ as $n \\\\to \\\\infty$ by the law of large numbers."}', 10, 0),
('b2010000-0000-0000-0000-000000000012', 'a0000000-0000-0000-0000-000000000010', 'paragraph', '{"text": "The time complexity is $\\\\mathcal{O}(n \\\\log n)$ in the average case and $\\\\mathcal{O}(n^2)$ in the worst case."}', 11, 0),
('b2010000-0000-0000-0000-000000000013', 'a0000000-0000-0000-0000-000000000010', 'paragraph', '{"text": "For all $\\\\varepsilon > 0$, there exists $\\\\delta > 0$ such that $|x - c| < \\\\delta$ implies $|f(x) - f(c)| < \\\\varepsilon$."}', 12, 0);

-- ============================================================
-- SYNTAX: Citations (replace existing with richer content)
-- ============================================================
DELETE FROM blocks WHERE document_id = 'a0000000-0000-0000-0000-000000000011';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b2011000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000011', 'paragraph', '{"text": "Citations link your text to bibliography entries. Type **\\\\cite{key}** in any paragraph to insert a citation marker that references your bibliography."}', 0, 0),
('b2011000-0000-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000011', 'heading', '{"text": "Citation Syntax", "level": 2}', 1, 0),
('b2011000-0000-0000-0000-000000000003', 'a0000000-0000-0000-0000-000000000011', 'table', '{"headers": ["You Type", "Purpose", "LaTeX Output"], "rows": [["\\\\cite{smith2023}", "Cite one source", "\\\\cite{smith2023}"], ["\\\\cite{a,b,c}", "Cite multiple sources", "\\\\cite{a,b,c}"], ["\\\\url{https://...}", "Insert a URL", "\\\\url{https://...}"], ["\\\\href{url}{text}", "Hyperlink with display text", "\\\\href{url}{text}"], ["\\\\footnote{note text}", "Add a footnote", "\\\\footnote{note text}"]], "caption": "Citation and link syntax"}', 2, 0),
('b2011000-0000-0000-0000-000000000004', 'a0000000-0000-0000-0000-000000000011', 'heading', '{"text": "Cross-References", "level": 2}', 3, 0),
('b2011000-0000-0000-0000-000000000005', 'a0000000-0000-0000-0000-000000000011', 'paragraph', '{"text": "Reference labeled blocks (equations, figures, tables, theorems) using **\\\\ref{label}**:"}', 4, 0),
('b2011000-0000-0000-0000-000000000006', 'a0000000-0000-0000-0000-000000000011', 'table', '{"headers": ["You Type", "References", "LaTeX Output"], "rows": [["\\\\ref{eq:momentum}", "A labeled equation", "\\\\ref{eq:momentum}"], ["\\\\ref{fig:architecture}", "A labeled figure", "\\\\ref{fig:architecture}"], ["\\\\ref{tbl:results}", "A labeled table", "\\\\ref{tbl:results}"], ["\\\\ref{thm:convergence}", "A labeled theorem", "\\\\ref{thm:convergence}"]], "caption": "Cross-reference syntax"}', 5, 0),
('b2011000-0000-0000-0000-000000000007', 'a0000000-0000-0000-0000-000000000011', 'heading', '{"text": "Setting Up Bibliography", "level": 2}', 6, 0),
('b2011000-0000-0000-0000-000000000008', 'a0000000-0000-0000-0000-000000000011', 'paragraph', '{"text": "For \\\\cite{} to work, you need a **Bibliography** block in your document:"}', 7, 0),
('b2011000-0000-0000-0000-000000000009', 'a0000000-0000-0000-0000-000000000011', 'list', '{"items": ["Add a Bibliography block (+ menu or /bibliography)", "Click edit to open the bibliography manager", "Add entries by DOI (paste a DOI, Lilia auto-fills all fields)", "Or add entries by ISBN for books", "Or enter manually: author, title, year, journal, etc.", "Note the cite_key — this is what you use in \\\\cite{}"], "ordered": true}', 8, 0),
('b2011000-0000-0000-0000-000000000010', 'a0000000-0000-0000-0000-000000000011', 'heading', '{"text": "Citation Styles", "level": 2}', 9, 0),
('b2011000-0000-0000-0000-000000000011', 'a0000000-0000-0000-0000-000000000011', 'table', '{"headers": ["Style", "In-Text Appearance", "Best For"], "rows": [["numeric / ieee", "[1], [2]", "Engineering, CS, physics"], ["apa", "(Smith, 2023)", "Psychology, social sciences"], ["authoryear", "Smith (2023)", "Humanities, natural language"], ["chicago", "(Smith 2023)", "History, arts"]], "caption": "Citation style options"}', 10, 0),
('b2011000-0000-0000-0000-000000000012', 'a0000000-0000-0000-0000-000000000011', 'heading', '{"text": "Example", "level": 2}', 11, 0),
('b2011000-0000-0000-0000-000000000013', 'a0000000-0000-0000-0000-000000000011', 'paragraph', '{"text": "The attention mechanism \\\\cite{vaswani2017} computes a weighted sum of value vectors. The scaling factor $1/\\\\sqrt{d_k}$ prevents the softmax from saturating \\\\cite{vaswani2017}. This achieves $\\\\mathcal{O}(1)$ sequential operations compared to $\\\\mathcal{O}(n)$ for recurrent models \\\\cite{gehring2017}."}', 12, 0);

-- ============================================================
-- SYNTAX: Text Formatting (replace existing with richer content)
-- ============================================================
DELETE FROM blocks WHERE document_id = 'a0000000-0000-0000-0000-000000000012';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b2012000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000012', 'paragraph', '{"text": "Format text using the **toolbar** (select text + click button) or by **typing markers** directly. Both produce the same result."}', 0, 0),
('b2012000-0000-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000012', 'heading', '{"text": "Formatting Reference", "level": 2}', 1, 0),
('b2012000-0000-0000-0000-000000000003', 'a0000000-0000-0000-0000-000000000012', 'table', '{"headers": ["Toolbar", "Type This", "Result", "LaTeX Output"], "rows": [["B", "**text**", "bold", "\\\\textbf{text}"], ["I", "*text*", "italic", "\\\\textit{text}"], ["U", "__text__", "underline", "\\\\underline{text}"], ["S", "~~text~~", "strikethrough", "\\\\sout{text}"], ["</>", "`text`", "monospace", "\\\\texttt{text}"], ["\u221a", "$E=mc^2$", "math", "$E=mc^2$"], ["\ud83d\udd17", "\\\\url{https://...}", "URL", "\\\\url{https://...}"], ["\u201c\u201d", "\\\\cite{key}", "citation", "\\\\cite{key}"]], "caption": "All formatting options"}', 2, 0),
('b2012000-0000-0000-0000-000000000004', 'a0000000-0000-0000-0000-000000000012', 'heading', '{"text": "How the Toolbar Works", "level": 2}', 3, 0),
('b2012000-0000-0000-0000-000000000005', 'a0000000-0000-0000-0000-000000000012', 'paragraph', '{"text": "Click the **\u270f\ufe0f edit** button on a paragraph card to open the drawer. The formatting toolbar appears at the top. Select text, then click a button to toggle formatting on/off."}', 4, 0),
('b2012000-0000-0000-0000-000000000006', 'a0000000-0000-0000-0000-000000000012', 'paragraph', '{"text": "The toolbar works with TipTap''s WYSIWYG editor — you see the formatting immediately. **Bold text looks bold**, *italic looks italic*, and $math$ renders with KaTeX."}', 5, 0),
('b2012000-0000-0000-0000-000000000007', 'a0000000-0000-0000-0000-000000000012', 'heading', '{"text": "Combining Formats", "level": 2}', 6, 0),
('b2012000-0000-0000-0000-000000000008', 'a0000000-0000-0000-0000-000000000012', 'paragraph', '{"text": "You can combine formatting freely: **bold with $inline math$** works, as does *italic with `code`*. Mix text formatting with citations: the seminal work by \\\\cite{smith2023} proved **convergence** for $\\\\alpha < 1$."}', 7, 0);

-- ============================================================
-- NEW: Display Equations Guide
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a2000000-0000-0000-0000-000000000001', 'system', 'Display Equations', true, 'syntax', 4, 'display-equations')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a2000000-0000-0000-0000-000000000001';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b2020000-0000-0000-0000-000000000001', 'a2000000-0000-0000-0000-000000000001', 'paragraph', '{"text": "Use an **Equation block** for formulas that deserve their own line — key results, derivations, and anything you want to number or reference."}', 0, 0),
('b2020000-0000-0000-0000-000000000002', 'a2000000-0000-0000-0000-000000000001', 'heading', '{"text": "Equation Modes", "level": 2}', 1, 0),
('b2020000-0000-0000-0000-000000000003', 'a2000000-0000-0000-0000-000000000001', 'table', '{"headers": ["Mode", "LaTeX Environment", "Use For"], "rows": [["display (default)", "\\\\begin{equation}...\\\\end{equation}", "Single equations, numbered"], ["display*", "\\\\begin{equation*}...\\\\end{equation*}", "Single equations, unnumbered"], ["align", "\\\\begin{align}...\\\\end{align}", "Multi-line aligned equations"], ["gather", "\\\\begin{gather}...\\\\end{gather}", "Multi-line centered equations"]], "caption": "Equation block modes"}', 2, 0),
('b2020000-0000-0000-0000-000000000004', 'a2000000-0000-0000-0000-000000000001', 'heading', '{"text": "Options", "level": 2}', 3, 0),
('b2020000-0000-0000-0000-000000000005', 'a2000000-0000-0000-0000-000000000001', 'table', '{"headers": ["Option", "Values", "Effect"], "rows": [["numbered", "true / false", "Shows equation number (1), (2), etc."], ["label", "string (e.g., eq:einstein)", "Enables cross-referencing with \\\\ref{eq:einstein}"], ["equationMode", "display / align / gather", "Changes the LaTeX environment"]], "caption": "Equation block options"}', 4, 0),
('b2020000-0000-0000-0000-000000000006', 'a2000000-0000-0000-0000-000000000001', 'heading', '{"text": "Examples", "level": 2}', 5, 0),
('b2020000-0000-0000-0000-000000000007', 'a2000000-0000-0000-0000-000000000001', 'paragraph', '{"text": "**Single equation (numbered, labeled):**"}', 6, 0),
('b2020000-0000-0000-0000-000000000008', 'a2000000-0000-0000-0000-000000000001', 'code', '{"code": "LaTeX in block: E = mc^2\nLabel: eq:einstein\nNumbered: true\n\nGenerates:\n\\\\begin{equation}\\\\label{eq:einstein}\nE = mc^2\n\\\\end{equation}", "language": "latex"}', 7, 0),
('b2020000-0000-0000-0000-000000000009', 'a2000000-0000-0000-0000-000000000001', 'paragraph', '{"text": "**Multi-line aligned:**"}', 8, 0),
('b2020000-0000-0000-0000-000000000010', 'a2000000-0000-0000-0000-000000000001', 'code', '{"code": "LaTeX in block:\na &= b + c \\\\\\\\\nd &= e + f\nMode: align\n\nGenerates:\n\\\\begin{align}\na &= b + c \\\\\\\\\nd &= e + f\n\\\\end{align}", "language": "latex"}', 9, 0),
('b2020000-0000-0000-0000-000000000011', 'a2000000-0000-0000-0000-000000000001', 'paragraph', '{"text": "Reference equations in text: \"As shown in Equation \\\\ref{eq:einstein}, the energy...\""}', 10, 0);

-- ============================================================
-- NEW: Headings Guide
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a2000000-0000-0000-0000-000000000002', 'system', 'Headings & Document Structure', true, 'syntax', 5, 'headings')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a2000000-0000-0000-0000-000000000002';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b2030000-0000-0000-0000-000000000001', 'a2000000-0000-0000-0000-000000000002', 'paragraph', '{"text": "Headings create your document''s section structure. They appear in the outline panel and generate LaTeX sectioning commands."}', 0, 0),
('b2030000-0000-0000-0000-000000000002', 'a2000000-0000-0000-0000-000000000002', 'heading', '{"text": "Heading Levels", "level": 2}', 1, 0),
('b2030000-0000-0000-0000-000000000003', 'a2000000-0000-0000-0000-000000000002', 'table', '{"headers": ["Level", "Use For", "LaTeX Output"], "rows": [["H1", "Document title / chapter title", "\\\\section{...}"], ["H2", "Major sections (Introduction, Methods, Results)", "\\\\subsection{...}"], ["H3", "Subsections within a section", "\\\\subsubsection{...}"], ["H4", "Minor subdivisions", "\\\\paragraph{...}"], ["H5-H6", "Rarely needed, fine-grained structure", "\\\\paragraph{...}"]], "caption": "Heading levels and their LaTeX mapping"}', 2, 0),
('b2030000-0000-0000-0000-000000000004', 'a2000000-0000-0000-0000-000000000002', 'heading', '{"text": "Typical Document Structures", "level": 2}', 3, 0),
('b2030000-0000-0000-0000-000000000005', 'a2000000-0000-0000-0000-000000000002', 'code', '{"code": "Research Paper:          Thesis Chapter:\nH1: Paper Title            H1: Chapter 3: Methods\n  H2: Abstract               H2: 3.1 Overview\n  H2: 1. Introduction         H2: 3.2 Data Collection\n  H2: 2. Methods                H3: 3.2.1 Survey Design\n    H3: 2.1 Data                H3: 3.2.2 Sampling\n    H3: 2.2 Analysis          H2: 3.3 Analysis\n  H2: 3. Results             H2: 3.4 Summary\n  H2: 4. Discussion\n  H2: 5. Conclusion\n  H2: References", "language": "text"}', 4, 0),
('b2030000-0000-0000-0000-000000000006', 'a2000000-0000-0000-0000-000000000002', 'heading', '{"text": "The Outline Panel", "level": 2}', 5, 0),
('b2030000-0000-0000-0000-000000000007', 'a2000000-0000-0000-0000-000000000002', 'paragraph', '{"text": "Click the outline icon in the activity bar (left side) to see your heading hierarchy. Click any heading in the outline to jump to that block. The outline updates in real-time as you add or edit headings."}', 6, 0);

-- ============================================================
-- NEW: Theorems & Proofs Guide
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a2000000-0000-0000-0000-000000000003', 'system', 'Theorems & Proofs', true, 'syntax', 6, 'theorems')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a2000000-0000-0000-0000-000000000003';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b2040000-0000-0000-0000-000000000001', 'a2000000-0000-0000-0000-000000000003', 'paragraph', '{"text": "The Theorem block supports 8 mathematical environments. Each generates the corresponding LaTeX environment with optional title and label."}', 0, 0),
('b2040000-0000-0000-0000-000000000002', 'a2000000-0000-0000-0000-000000000003', 'heading', '{"text": "Theorem Types", "level": 2}', 1, 0),
('b2040000-0000-0000-0000-000000000003', 'a2000000-0000-0000-0000-000000000003', 'table', '{"headers": ["Type", "LaTeX Environment", "Typical Use"], "rows": [["theorem", "\\\\begin{theorem}", "Main results"], ["lemma", "\\\\begin{lemma}", "Supporting results"], ["proposition", "\\\\begin{proposition}", "Less major results"], ["corollary", "\\\\begin{corollary}", "Consequences of a theorem"], ["definition", "\\\\begin{definition}", "Formal definitions"], ["example", "\\\\begin{example}", "Illustrative examples"], ["remark", "\\\\begin{remark}", "Informal observations"], ["proof", "\\\\begin{proof}", "Proofs (ends with QED \u25a1)"]], "caption": "All theorem block types"}', 2, 0),
('b2040000-0000-0000-0000-000000000004', 'a2000000-0000-0000-0000-000000000003', 'heading', '{"text": "Content Fields", "level": 2}', 3, 0),
('b2040000-0000-0000-0000-000000000005', 'a2000000-0000-0000-0000-000000000003', 'table', '{"headers": ["Field", "Required", "Description", "LaTeX Effect"], "rows": [["theoremType", "Yes", "Which environment to use", "\\\\begin{theorem} vs \\\\begin{lemma} etc."], ["title", "No", "Optional title in brackets", "[Title] after \\\\begin{theorem}"], ["text", "Yes", "The statement/proof text", "Body content (supports $math$)"], ["label", "No", "For cross-referencing", "\\\\label{thm:name} inside the environment"]], "caption": "Theorem block content fields"}', 4, 0),
('b2040000-0000-0000-0000-000000000006', 'a2000000-0000-0000-0000-000000000003', 'heading', '{"text": "LaTeX Output Examples", "level": 2}', 5, 0),
('b2040000-0000-0000-0000-000000000007', 'a2000000-0000-0000-0000-000000000003', 'code', '{"code": "Theorem with title and label:\n\\\\begin{theorem}[Convergence Rate]\\\\label{thm:convergence}\nUnder smoothness assumptions, SGD achieves\n$\\\\mathcal{O}(1/\\\\sqrt{T})$ convergence.\n\\\\end{theorem}\n\nDefinition:\n\\\\begin{definition}[Convex Function]\\\\label{def:convex}\nA function $f$ is convex if for all $x, y$ and\n$\\\\lambda \\\\in [0,1]$: $f(\\\\lambda x + (1-\\\\lambda)y)\n\\\\leq \\\\lambda f(x) + (1-\\\\lambda)f(y)$.\n\\\\end{definition}\n\nProof (auto QED):\n\\\\begin{proof}\nBy contradiction, assume $\\\\sqrt{2} = p/q$...\n\\\\end{proof}", "language": "latex"}', 6, 0),
('b2040000-0000-0000-0000-000000000008', 'a2000000-0000-0000-0000-000000000003', 'paragraph', '{"text": "Reference theorems in text: \"By Theorem \\\\ref{thm:convergence}, the sequence converges.\""}', 7, 0);

-- ============================================================
-- NEW: Code Blocks Guide
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a2000000-0000-0000-0000-000000000004', 'system', 'Code Blocks', true, 'syntax', 7, 'code-blocks')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a2000000-0000-0000-0000-000000000004';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b2050000-0000-0000-0000-000000000001', 'a2000000-0000-0000-0000-000000000004', 'paragraph', '{"text": "Add source code with syntax highlighting. The Code block supports 20+ languages and generates a LaTeX lstlisting environment."}', 0, 0),
('b2050000-0000-0000-0000-000000000002', 'a2000000-0000-0000-0000-000000000004', 'heading', '{"text": "Supported Languages", "level": 2}', 1, 0),
('b2050000-0000-0000-0000-000000000003', 'a2000000-0000-0000-0000-000000000004', 'paragraph', '{"text": "Python, JavaScript, TypeScript, Java, C, C++, C#, Go, Rust, Ruby, PHP, SQL, R, MATLAB, LaTeX, HTML, CSS, Bash, Pseudocode, and more."}', 2, 0),
('b2050000-0000-0000-0000-000000000004', 'a2000000-0000-0000-0000-000000000004', 'heading', '{"text": "Content Fields", "level": 2}', 3, 0),
('b2050000-0000-0000-0000-000000000005', 'a2000000-0000-0000-0000-000000000004', 'table', '{"headers": ["Field", "Type", "Description", "LaTeX Effect"], "rows": [["code", "string", "The source code", "Content inside lstlisting"], ["language", "string", "Programming language", "language= option in lstlisting"]], "caption": "Code block content fields"}', 4, 0),
('b2050000-0000-0000-0000-000000000006', 'a2000000-0000-0000-0000-000000000004', 'heading', '{"text": "LaTeX Output", "level": 2}', 5, 0),
('b2050000-0000-0000-0000-000000000007', 'a2000000-0000-0000-0000-000000000004', 'code', '{"code": "\\\\begin{lstlisting}[language=python]\ndef fibonacci(n):\n    if n <= 1:\n        return n\n    return fibonacci(n-1) + fibonacci(n-2)\n\\\\end{lstlisting}", "language": "latex"}', 6, 0);

-- ============================================================
-- NEW: Lists Guide
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a2000000-0000-0000-0000-000000000005', 'system', 'Lists', true, 'syntax', 8, 'lists')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a2000000-0000-0000-0000-000000000005';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b2060000-0000-0000-0000-000000000001', 'a2000000-0000-0000-0000-000000000005', 'paragraph', '{"text": "Add bullet or numbered lists. Items support inline formatting and $math$."}', 0, 0),
('b2060000-0000-0000-0000-000000000002', 'a2000000-0000-0000-0000-000000000005', 'heading', '{"text": "Content Fields", "level": 2}', 1, 0),
('b2060000-0000-0000-0000-000000000003', 'a2000000-0000-0000-0000-000000000005', 'table', '{"headers": ["Field", "Type", "Default", "LaTeX Effect"], "rows": [["items", "string[]", "[]", "Each item becomes \\\\item"], ["ordered", "boolean", "false", "itemize (false) or enumerate (true)"], ["start", "number", "1", "Start number for ordered lists"]], "caption": "List block content fields"}', 2, 0),
('b2060000-0000-0000-0000-000000000004', 'a2000000-0000-0000-0000-000000000005', 'heading', '{"text": "LaTeX Output", "level": 2}', 3, 0),
('b2060000-0000-0000-0000-000000000005', 'a2000000-0000-0000-0000-000000000005', 'code', '{"code": "Unordered (ordered: false):\n\\\\begin{itemize}\n\\\\item First item with **bold**\n\\\\item Second with $math$\n\\\\item Third item\n\\\\end{itemize}\n\nOrdered (ordered: true):\n\\\\begin{enumerate}\n\\\\item Step one\n\\\\item Step two\n\\\\item Step three\n\\\\end{enumerate}", "language": "latex"}', 4, 0);

-- ============================================================
-- NEW: Figures & Images Guide
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a2000000-0000-0000-0000-000000000006', 'system', 'Figures & Images', true, 'syntax', 9, 'figures')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a2000000-0000-0000-0000-000000000006';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b2070000-0000-0000-0000-000000000001', 'a2000000-0000-0000-0000-000000000006', 'paragraph', '{"text": "Add images with captions, labels, and size control. Upload from your device or paste a URL."}', 0, 0),
('b2070000-0000-0000-0000-000000000002', 'a2000000-0000-0000-0000-000000000006', 'heading', '{"text": "Content Fields", "level": 2}', 1, 0),
('b2070000-0000-0000-0000-000000000003', 'a2000000-0000-0000-0000-000000000006', 'table', '{"headers": ["Field", "Type", "Default", "LaTeX Effect"], "rows": [["src", "string (URL)", "required", "\\\\includegraphics{filename}"], ["caption", "string", "\"\"", "\\\\caption{text}"], ["alt", "string", "\"\"", "Alt text (accessibility)"], ["width", "number (0-1)", "0.8", "width=0.8\\\\textwidth"], ["label", "string", "\"\"", "\\\\label{fig:name}"], ["placement", "string", "auto", "[H], [htbp], etc."]], "caption": "Figure block content fields"}', 2, 0),
('b2070000-0000-0000-0000-000000000004', 'a2000000-0000-0000-0000-000000000006', 'heading', '{"text": "LaTeX Output", "level": 2}', 3, 0),
('b2070000-0000-0000-0000-000000000005', 'a2000000-0000-0000-0000-000000000006', 'code', '{"code": "\\\\begin{figure}[H]\n\\\\centering\n\\\\includegraphics[width=0.8\\\\textwidth]{figures/architecture.png}\n\\\\caption{System architecture overview}\n\\\\label{fig:architecture}\n\\\\end{figure}", "language": "latex"}', 4, 0),
('b2070000-0000-0000-0000-000000000006', 'a2000000-0000-0000-0000-000000000006', 'paragraph', '{"text": "Reference in text: \"See Figure \\\\ref{fig:architecture} for the system overview.\""}', 5, 0);
