-- Seed help content articles
-- Help articles are documents with is_help_content = true, owned by 'system'
-- Each article has blocks for its content

-- Ensure system user exists
INSERT INTO users (id, external_id, email, display_name)
VALUES ('system', 'system', 'system@lilia.app', 'Lilia')
ON CONFLICT (id) DO NOTHING;

-- ============================================================
-- CATEGORY: getting-started
-- ============================================================

-- Article: What is a Block?
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a0000000-0000-0000-0000-000000000001', 'system', 'What is a Block?', true, 'getting-started', 1, 'what-is-a-block');

INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b0000000-0001-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000001', 'paragraph', '{"text": "A Lilia document is a **stack of blocks**. Each block has a type (paragraph, heading, equation, figure, table, etc.) and a content payload. You build your document by adding, editing, and reordering blocks."}', 0, 0),
('b0000000-0001-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000001', 'heading', '{"text": "Block Types", "level": 2}', 1, 0),
('b0000000-0001-0000-0000-000000000003', 'a0000000-0000-0000-0000-000000000001', 'table', '{"headers": ["Block", "Purpose", "Shortcut"], "rows": [["Paragraph", "Rich text with inline formatting", "Just start typing"], ["Heading", "Section titles (H1-H6)", "/heading"], ["Equation", "Display or inline math (LaTeX)", "/equation"], ["Figure", "Images with captions", "/figure"], ["Table", "Data tables with headers", "/table"], ["Code", "Syntax-highlighted code", "/code"], ["List", "Bullet or numbered lists", "/list"], ["Theorem", "Theorems, lemmas, proofs", "/theorem"], ["Abstract", "Paper abstract", "/abstract"], ["Bibliography", "References section", "/bibliography"]], "caption": "Common block types"}', 2, 0),
('b0000000-0001-0000-0000-000000000004', 'a0000000-0000-0000-0000-000000000001', 'heading', '{"text": "Adding Blocks", "level": 2}', 3, 0),
('b0000000-0001-0000-0000-000000000005', 'a0000000-0000-0000-0000-000000000001', 'paragraph', '{"text": "There are three ways to add a block: the **+ button** between blocks, the **/ slash command** in an empty paragraph, or starting from a **template** in the gallery."}', 4, 0);

-- Article: Templates Overview
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a0000000-0000-0000-0000-000000000002', 'system', 'Templates Overview', true, 'getting-started', 2, 'templates');

INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b0000000-0002-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000002', 'paragraph', '{"text": "Start faster with a pre-built template. Each template sets up a document structure with the right block types already in place."}', 0, 0),
('b0000000-0002-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000002', 'table', '{"headers": ["Template", "Best For"], "rows": [["Blank Document", "Anything — start from scratch"], ["Academic Paper", "Research papers, conference submissions"], ["Physics Homework", "Problem sets with equation blocks"], ["Thesis Chapter", "Dissertation chapters with sections"], ["Math Proof", "Theorem-proof style documents"], ["Lab Report", "Experimental science reports"]], "caption": "Available templates"}', 1, 0);

-- ============================================================
-- CATEGORY: syntax
-- ============================================================

-- Article: Inline Math
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a0000000-0000-0000-0000-000000000010', 'system', 'Inline Math ($...$)', true, 'syntax', 1, 'inline-math');

INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b0000000-0010-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000010', 'paragraph', '{"text": "Inline math lives inside a paragraph. Wrap any LaTeX expression in **dollar signs** to render it as math."}', 0, 0),
('b0000000-0010-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000010', 'heading', '{"text": "Syntax Reference", "level": 2}', 1, 0),
('b0000000-0010-0000-0000-000000000003', 'a0000000-0000-0000-0000-000000000010', 'table', '{"headers": ["What you type", "What you see", "LaTeX output"], "rows": [["$E = mc^2$", "E = mc²", "$E = mc^2$"], ["$\\\\frac{a}{b}$", "a/b (fraction)", "\\\\frac{a}{b}"], ["$\\\\sqrt{x}$", "√x", "\\\\sqrt{x}"], ["$\\\\alpha, \\\\beta, \\\\theta$", "α, β, θ", "\\\\alpha, \\\\beta, \\\\theta"], ["$\\\\sum_{i=1}^{n} x_i$", "Σ x_i", "\\\\sum_{i=1}^{n} x_i"], ["$\\\\int_0^1 f(x)\\\\,dx$", "∫ f(x) dx", "\\\\int_0^1 f(x)\\\\,dx"], ["$\\\\mathbb{R}$", "ℝ (blackboard)", "\\\\mathbb{R}"], ["$\\\\mathcal{O}(n)$", "𝒪(n) (calligraphic)", "\\\\mathcal{O}(n)"], ["$\\\\|x\\\\|$", "‖x‖ (norm)", "\\\\|x\\\\|"], ["$x^2 + y^2 = r^2$", "x² + y² = r²", "$x^2 + y^2 = r^2$"]], "caption": "Inline math syntax"}', 2, 0),
('b0000000-0010-0000-0000-000000000004', 'a0000000-0000-0000-0000-000000000010', 'heading', '{"text": "Examples in Context", "level": 2}', 3, 0),
('b0000000-0010-0000-0000-000000000005', 'a0000000-0000-0000-0000-000000000010', 'paragraph', '{"text": "The energy-mass equivalence $E = mc^2$ was proposed by Einstein in 1905."}', 4, 0),
('b0000000-0010-0000-0000-000000000006', 'a0000000-0000-0000-0000-000000000010', 'paragraph', '{"text": "Let $f: \\\\mathbb{R} \\\\to \\\\mathbb{R}$ be a continuous function on $[a, b]$."}', 5, 0),
('b0000000-0010-0000-0000-000000000007', 'a0000000-0000-0000-0000-000000000010', 'paragraph', '{"text": "The sample mean is $\\\\bar{x} = \\\\frac{1}{n}\\\\sum_{i=1}^{n} x_i$ where $n$ is the sample size."}', 6, 0),
('b0000000-0010-0000-0000-000000000008', 'a0000000-0000-0000-0000-000000000010', 'heading', '{"text": "When to Use Inline vs Display", "level": 2}', 7, 0),
('b0000000-0010-0000-0000-000000000009', 'a0000000-0000-0000-0000-000000000010', 'table', '{"headers": ["Use inline $...$ when", "Use an Equation block when"], "rows": [["The expression is short", "The expression is a key result"], ["It flows within a sentence", "It needs a label for \\\\ref{}"], ["It''s a variable like $x$ or $\\\\theta$", "It''s a multi-line derivation"], ["It''s a simple formula", "You want it numbered"]]}', 8, 0);

-- Article: Citations
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a0000000-0000-0000-0000-000000000011', 'system', 'Citations (\cite{})', true, 'syntax', 2, 'citations');

INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b0000000-0011-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000011', 'paragraph', '{"text": "Citations reference your bibliography entries. Use **\\\\cite{key}** in any paragraph to insert a citation marker."}', 0, 0),
('b0000000-0011-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000011', 'heading', '{"text": "Syntax Reference", "level": 2}', 1, 0),
('b0000000-0011-0000-0000-000000000003', 'a0000000-0000-0000-0000-000000000011', 'table', '{"headers": ["What you type", "Purpose", "LaTeX output"], "rows": [["\\\\cite{smith2023}", "Single citation", "\\\\cite{smith2023}"], ["\\\\cite{a,b,c}", "Multiple citations", "\\\\cite{a,b,c}"], ["\\\\ref{eq:label}", "Cross-reference equation", "\\\\ref{eq:label}"], ["\\\\ref{fig:label}", "Cross-reference figure", "\\\\ref{fig:label}"], ["\\\\ref{tbl:label}", "Cross-reference table", "\\\\ref{tbl:label}"], ["\\\\ref{thm:label}", "Cross-reference theorem", "\\\\ref{thm:label}"], ["\\\\url{https://...}", "Insert URL", "\\\\url{https://...}"], ["\\\\href{url}{text}", "Hyperlink with text", "\\\\href{url}{text}"], ["\\\\footnote{text}", "Footnote", "\\\\footnote{text}"]], "caption": "Citation and reference syntax"}', 2, 0),
('b0000000-0011-0000-0000-000000000004', 'a0000000-0000-0000-0000-000000000011', 'heading', '{"text": "Example", "level": 2}', 3, 0),
('b0000000-0011-0000-0000-000000000005', 'a0000000-0000-0000-0000-000000000011', 'paragraph', '{"text": "Gradient descent was first analyzed in a stochastic setting by \\\\cite{robbins1951}. Building on this, \\\\cite{polyak1964} introduced momentum to accelerate convergence."}', 4, 0),
('b0000000-0011-0000-0000-000000000006', 'a0000000-0000-0000-0000-000000000011', 'heading', '{"text": "Requirements", "level": 2}', 5, 0),
('b0000000-0011-0000-0000-000000000007', 'a0000000-0000-0000-0000-000000000011', 'paragraph', '{"text": "The citation key (e.g., robbins1951) must match a **cite_key** in your Bibliography block. Add a Bibliography block to your document, then use DOI or ISBN lookup to auto-fill entries."}', 6, 0);

-- Article: Formatting
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a0000000-0000-0000-0000-000000000012', 'system', 'Text Formatting', true, 'syntax', 3, 'formatting');

INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b0000000-0012-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000012', 'paragraph', '{"text": "Format text using the **toolbar buttons** or by typing markdown-style markers directly."}', 0, 0),
('b0000000-0012-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000012', 'heading', '{"text": "Syntax Reference", "level": 2}', 1, 0),
('b0000000-0012-0000-0000-000000000003', 'a0000000-0000-0000-0000-000000000012', 'table', '{"headers": ["What you type", "What you see", "LaTeX output"], "rows": [["**bold text**", "bold text", "\\\\textbf{bold text}"], ["*italic text*", "italic text", "\\\\textit{italic text}"], ["__underlined__", "underlined", "\\\\underline{underlined}"], ["~~strikethrough~~", "strikethrough", "\\\\sout{strikethrough}"], ["`inline code`", "inline code", "\\\\texttt{inline code}"], ["$E = mc^2$", "E = mc²", "$E = mc^2$"]], "caption": "Inline formatting syntax"}', 2, 0),
('b0000000-0012-0000-0000-000000000004', 'a0000000-0000-0000-0000-000000000012', 'heading', '{"text": "Toolbar vs Typing", "level": 2}', 3, 0),
('b0000000-0012-0000-0000-000000000005', 'a0000000-0000-0000-0000-000000000012', 'paragraph', '{"text": "Both methods work identically. The toolbar applies formatting to selected text. Typing markers (like **bold**) gets parsed by the editor into rich text automatically. Use whichever feels natural."}', 4, 0);

-- ============================================================
-- CATEGORY: tutorials
-- ============================================================

-- Article: Academic Paper
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a0000000-0000-0000-0000-000000000020', 'system', 'Academic Paper', true, 'tutorials', 1, 'tutorial-academic-paper');

INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b0000000-0020-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000020', 'paragraph', '{"text": "The most common document type. Here''s the block structure for a research paper:"}', 0, 0),
('b0000000-0020-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000020', 'code', '{"code": "H1       → Paper Title\\nAbstract → Your abstract\\nH2       → 1. Introduction\\nParagraph → Text with \\\\cite{} citations\\nH2       → 2. Methods\\nEquation → Key equations (labeled)\\nH2       → 3. Results\\nTable    → Experimental data\\nFigure   → Charts or diagrams\\nH2       → 4. Discussion\\nH2       → 5. Conclusion\\nBibliography → References", "language": "text"}', 1, 0),
('b0000000-0020-0000-0000-000000000003', 'a0000000-0000-0000-0000-000000000020', 'heading', '{"text": "Tips", "level": 2}', 2, 0),
('b0000000-0020-0000-0000-000000000004', 'a0000000-0000-0000-0000-000000000020', 'paragraph', '{"text": "Use $\\\\mathcal{O}(n)$ for complexity notation inline. Label equations with eq: prefix for cross-referencing: \\\\ref{eq:momentum}. Add bibliography entries via DOI lookup."}', 3, 0);

-- Article: Math Homework
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a0000000-0000-0000-0000-000000000021', 'system', 'Math Homework', true, 'tutorials', 2, 'tutorial-math-homework');

INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b0000000-0021-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000021', 'paragraph', '{"text": "Problem sets with proofs and derivations. Use inline math heavily — most proof text flows naturally with $...$ expressions."}', 0, 0),
('b0000000-0021-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000021', 'code', '{"code": "H1       → MATH 301 — Problem Set 4\\nParagraph → Name / Date / Course\\nH2       → Problem 1\\nParagraph → Problem statement with $math$\\nH3       → Solution\\nParagraph → Proof with inline $math$\\nEquation → Key derivation steps\\nH2       → Problem 2\\n...", "language": "text"}', 1, 0),
('b0000000-0021-0000-0000-000000000003', 'a0000000-0000-0000-0000-000000000021', 'heading', '{"text": "Example", "level": 2}', 2, 0),
('b0000000-0021-0000-0000-000000000004', 'a0000000-0000-0000-0000-000000000021', 'paragraph', '{"text": "Prove that every convergent sequence in $\\\\mathbb{R}$ is bounded. Let $(a_n) \\\\to L$. Then $\\\\exists\\\\, N$ such that $|a_n - L| < 1$ for all $n \\\\geq N$. Define $M = \\\\max\\\\{|a_1|, \\\\ldots, |a_{N-1}|, 1 + |L|\\\\}$. Then $|a_n| \\\\leq M$ for all $n$."}', 3, 0);

-- Article: Lab Report
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a0000000-0000-0000-0000-000000000022', 'system', 'Lab Report', true, 'tutorials', 3, 'tutorial-lab-report');

INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b0000000-0022-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000022', 'paragraph', '{"text": "For experimental science — physics, chemistry, biology. Structured with objective, theory, methods, results, and analysis."}', 0, 0),
('b0000000-0022-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000022', 'code', '{"code": "H1       → Lab Report: Experiment Title\\nH2       → Objective\\nParagraph → What you''re testing\\nH2       → Theory\\nEquation → Key formula (e.g., V = IR)\\nH2       → Materials\\nList     → Equipment list\\nH2       → Results\\nTable    → Measurements\\nH2       → Analysis\\nEquation → Percent error formula\\nH2       → Conclusion", "language": "text"}', 1, 0);

-- ============================================================
-- CATEGORY: reference
-- ============================================================

-- Article: LaTeX Math Cheat Sheet
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a0000000-0000-0000-0000-000000000030', 'system', 'LaTeX Math Cheat Sheet', true, 'reference', 1, 'latex-cheat-sheet');

INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b0000000-0030-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000030', 'paragraph', '{"text": "Quick reference for common LaTeX math expressions. Use these inside $...$ for inline or in Equation blocks for display."}', 0, 0),
('b0000000-0030-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000030', 'heading', '{"text": "Basics", "level": 2}', 1, 0),
('b0000000-0030-0000-0000-000000000003', 'a0000000-0000-0000-0000-000000000030', 'table', '{"headers": ["What you want", "Type this"], "rows": [["Fraction", "\\\\frac{a}{b}"], ["Square root", "\\\\sqrt{x} or \\\\sqrt[n]{x}"], ["Subscript", "x_i or x_{ij}"], ["Superscript", "x^2 or x^{n+1}"], ["Greek letters", "\\\\alpha, \\\\beta, \\\\gamma, \\\\theta, \\\\lambda, \\\\sigma, \\\\pi, \\\\omega"], ["Bold math", "\\\\mathbf{x} or \\\\boldsymbol{\\\\theta}"], ["Calligraphic", "\\\\mathcal{O}(n)"], ["Blackboard bold", "\\\\mathbb{R}, \\\\mathbb{Z}, \\\\mathbb{N}, \\\\mathbb{C}"]]}', 2, 0),
('b0000000-0030-0000-0000-000000000004', 'a0000000-0000-0000-0000-000000000030', 'heading', '{"text": "Operators & Relations", "level": 2}', 3, 0),
('b0000000-0030-0000-0000-000000000005', 'a0000000-0000-0000-0000-000000000030', 'table', '{"headers": ["What you want", "Type this"], "rows": [["Summation", "\\\\sum_{i=1}^{n} x_i"], ["Product", "\\\\prod_{i=1}^{n} x_i"], ["Integral", "\\\\int_a^b f(x)\\\\,dx"], ["Partial derivative", "\\\\frac{\\\\partial f}{\\\\partial x}"], ["Limit", "\\\\lim_{x \\\\to \\\\infty} f(x)"], ["Norm", "\\\\|x\\\\| or \\\\lVert x \\\\rVert"], ["Floor/Ceiling", "\\\\lfloor x \\\\rfloor, \\\\lceil x \\\\rceil"], ["Dot/Hat/Bar/Tilde", "\\\\dot{x}, \\\\hat{x}, \\\\bar{x}, \\\\tilde{x}"]]}', 4, 0),
('b0000000-0030-0000-0000-000000000006', 'a0000000-0000-0000-0000-000000000030', 'heading', '{"text": "Environments", "level": 2}', 5, 0),
('b0000000-0030-0000-0000-000000000007', 'a0000000-0000-0000-0000-000000000030', 'table', '{"headers": ["What you want", "Type this"], "rows": [["Matrix (round)", "\\\\begin{pmatrix} a & b \\\\\\\\ c & d \\\\end{pmatrix}"], ["Matrix (square)", "\\\\begin{bmatrix} a & b \\\\\\\\ c & d \\\\end{bmatrix}"], ["Cases", "\\\\begin{cases} x & \\\\text{if } x > 0 \\\\\\\\ -x & \\\\text{otherwise} \\\\end{cases}"], ["Aligned equations", "\\\\begin{aligned} a &= b + c \\\\\\\\ d &= e + f \\\\end{aligned}"]]}', 6, 0);

-- Article: Common Mistakes
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a0000000-0000-0000-0000-000000000031', 'system', 'Common Mistakes', true, 'reference', 2, 'common-mistakes');

INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b0000000-0031-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000031', 'paragraph', '{"text": "Avoid these common pitfalls when writing in Lilia:"}', 0, 0),
('b0000000-0031-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000031', 'table', '{"headers": ["Mistake", "Why", "Fix"], "rows": [["Writing $E=mc^2$ in Equation blocks", "Equation blocks are already in math mode", "Just write E=mc^2 without dollar signs"], ["Using $$ for display math in paragraphs", "Lilia doesn''t support $$...$$ syntax", "Use a separate Equation block instead"], ["Forgetting \\\\text{} for words in math", "Words in math mode have no spacing", "Use $x > 0 \\\\text{ and } y < 1$"], ["Missing \\\\, in integrals", "dx looks attached to the integrand", "Write \\\\int f(x)\\\\,dx not \\\\int f(x)dx"], ["Using * for multiplication in math", "The * conflicts with italic markup", "Use $a \\\\times b$ or $a \\\\cdot b$ instead"], ["Labels without prefixes", "Hard to tell eq from fig references", "Use eq:, fig:, tbl:, thm: prefixes"]]}', 1, 0);

-- Article: Block Content Fields
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a0000000-0000-0000-0000-000000000032', 'system', 'Block Content Reference', true, 'reference', 3, 'block-content-reference');

INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b0000000-0032-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000032', 'paragraph', '{"text": "Each block type stores its content in a JSON object. Here are the key fields for each type:"}', 0, 0),
('b0000000-0032-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000032', 'code', '{"code": "paragraph:    { text: \"...\" }\\nheading:      { text: \"...\", level: 1-6 }\\nequation:     { latex: \"...\", numbered: true, label: \"eq:name\" }\\nfigure:       { src: \"url\", caption: \"...\", width: 0.8, label: \"fig:name\" }\\ntable:        { headers: [...], rows: [[...]], caption: \"...\" }\\ncode:         { code: \"...\", language: \"python\" }\\nlist:         { items: [...], ordered: false }\\nblockquote:   { text: \"...\" }\\ntheorem:      { theoremType: \"theorem\", title: \"...\", text: \"...\", label: \"thm:name\" }\\nabstract:     { text: \"...\" }\\nbibliography: { style: \"apa\", entries: [...] }", "language": "text"}', 1, 0),
('b0000000-0032-0000-0000-000000000003', 'a0000000-0000-0000-0000-000000000032', 'heading', '{"text": "Theorem Types", "level": 2}', 2, 0),
('b0000000-0032-0000-0000-000000000004', 'a0000000-0000-0000-0000-000000000032', 'paragraph', '{"text": "The theorem block supports: **theorem**, **lemma**, **proposition**, **corollary**, **definition**, **example**, **remark**, **proof**. Set via the theoremType field."}', 3, 0);
