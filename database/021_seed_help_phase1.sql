-- Phase 1: Getting Started — 8 sequential articles for new users
-- Also replaces/updates the syntax and reference articles with richer content
-- Run AFTER 020_seed_help_content.sql

-- ============================================================
-- GETTING STARTED 1: Welcome to Lilia
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a1000000-0000-0000-0000-000000000001', 'system', 'Welcome to Lilia', true, 'getting-started', 0, 'welcome')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a1000000-0000-0000-0000-000000000001';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b1000001-0000-0000-0000-000000000001', 'a1000000-0000-0000-0000-000000000001', 'paragraph', '{"text": "Lilia is an editor for **academic and technical documents** — research papers, theses, lab reports, math homework, and more. It combines the simplicity of a modern editor with the power of LaTeX."}', 0, 0),
('b1000001-0000-0000-0000-000000000002', 'a1000000-0000-0000-0000-000000000001', 'paragraph', '{"text": "You don''t need to know LaTeX to use Lilia. Write in rich text, add equations with $math$, insert citations with \\\\cite{key}, and Lilia generates publication-ready LaTeX automatically."}', 1, 0),
('b1000001-0000-0000-0000-000000000003', 'a1000000-0000-0000-0000-000000000001', 'heading', '{"text": "What You Can Build", "level": 2}', 2, 0),
('b1000001-0000-0000-0000-000000000004', 'a1000000-0000-0000-0000-000000000001', 'table', '{"headers": ["Document Type", "Key Features"], "rows": [["Research Paper", "Abstract, sections, equations, bibliography with \\\\cite{}"], ["Thesis Chapter", "Multi-section structure, figures, theorems, cross-references"], ["Math Homework", "Problem sets with inline $math$ and display equations"], ["Lab Report", "Objective, methods, results tables, error analysis"], ["Technical Report", "Code blocks, complexity tables, algorithm descriptions"]], "caption": ""}', 3, 0),
('b1000001-0000-0000-0000-000000000005', 'a1000000-0000-0000-0000-000000000001', 'heading', '{"text": "How This Guide Works", "level": 2}', 4, 0),
('b1000001-0000-0000-0000-000000000006', 'a1000000-0000-0000-0000-000000000001', 'paragraph', '{"text": "Follow the **Getting Started** articles in order — they''ll take you from creating your first document to exporting it as LaTeX in about 15 minutes. Then explore the **Feature Guides** and **Reference** sections as needed."}', 5, 0);

-- ============================================================
-- GETTING STARTED 2: Create Your First Document
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a1000000-0000-0000-0000-000000000002', 'system', 'Create Your First Document', true, 'getting-started', 1, 'create-first-document')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a1000000-0000-0000-0000-000000000002';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b1000002-0000-0000-0000-000000000001', 'a1000000-0000-0000-0000-000000000002', 'heading', '{"text": "Step 1: From the Dashboard", "level": 2}', 0, 0),
('b1000002-0000-0000-0000-000000000002', 'a1000000-0000-0000-0000-000000000002', 'paragraph', '{"text": "Click **New Document** on your dashboard. You''ll see a template picker with options like Blank Document, Academic Paper, Physics Homework, Thesis Chapter, Math Proof, and Lab Report."}', 1, 0),
('b1000002-0000-0000-0000-000000000003', 'a1000000-0000-0000-0000-000000000002', 'paragraph', '{"text": "For your first document, pick **Blank Document** — we''ll add everything from scratch so you understand how blocks work."}', 2, 0),
('b1000002-0000-0000-0000-000000000004', 'a1000000-0000-0000-0000-000000000002', 'heading', '{"text": "Step 2: The Studio", "level": 2}', 3, 0),
('b1000002-0000-0000-0000-000000000005', 'a1000000-0000-0000-0000-000000000002', 'paragraph', '{"text": "You''re now in the **Studio** — Lilia''s main editing view. Here''s what you see:"}', 4, 0),
('b1000002-0000-0000-0000-000000000006', 'a1000000-0000-0000-0000-000000000002', 'table', '{"headers": ["Area", "What It Does"], "rows": [["Top bar", "Document title, mode tabs (Studio, Preview, Validate, Flow)"], ["Activity bar (left)", "Toggle outline, comments, and docs panels"], ["Main area", "Your block cards — the document content"], ["+ Add block", "Insert new blocks at the end"]], "caption": "Studio layout"}', 5, 0),
('b1000002-0000-0000-0000-000000000007', 'a1000000-0000-0000-0000-000000000002', 'heading', '{"text": "Step 3: Name Your Document", "level": 2}', 6, 0),
('b1000002-0000-0000-0000-000000000008', 'a1000000-0000-0000-0000-000000000002', 'paragraph', '{"text": "Click the title at the top (it says \"Untitled\") and type your document title. This becomes the \\\\title{} in your LaTeX output."}', 7, 0);

-- ============================================================
-- GETTING STARTED 3: Understanding Blocks
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a1000000-0000-0000-0000-000000000003', 'system', 'Understanding Blocks', true, 'getting-started', 2, 'understand-blocks')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a1000000-0000-0000-0000-000000000003';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b1000003-0000-0000-0000-000000000001', 'a1000000-0000-0000-0000-000000000003', 'paragraph', '{"text": "A Lilia document is a **stack of blocks**. Each block is a content unit with a type — paragraph, heading, equation, figure, table, code, etc."}', 0, 0),
('b1000003-0000-0000-0000-000000000002', 'a1000000-0000-0000-0000-000000000003', 'paragraph', '{"text": "Think of blocks like building blocks. A research paper might be: Heading → Abstract → Heading → Paragraph → Equation → Heading → Table → Bibliography."}', 1, 0),
('b1000003-0000-0000-0000-000000000003', 'a1000000-0000-0000-0000-000000000003', 'heading', '{"text": "Block Types", "level": 2}', 2, 0),
('b1000003-0000-0000-0000-000000000004', 'a1000000-0000-0000-0000-000000000003', 'table', '{"headers": ["Block", "What It Does", "LaTeX Output"], "rows": [["Paragraph", "Rich text with bold, italic, math, citations", "Plain text with \\\\textbf{}, \\\\textit{}, $math$"], ["Heading", "Section titles (H1–H6)", "\\\\section{}, \\\\subsection{}, etc."], ["Equation", "Display math (LaTeX)", "\\\\begin{equation}...\\\\end{equation}"], ["Figure", "Images with captions", "\\\\begin{figure}...\\\\end{figure}"], ["Table", "Data tables with headers", "\\\\begin{table}...\\\\end{table}"], ["Code", "Syntax-highlighted code", "\\\\begin{lstlisting}...\\\\end{lstlisting}"], ["List", "Bullet or numbered items", "\\\\begin{itemize} or \\\\begin{enumerate}"], ["Theorem", "Theorems, lemmas, proofs", "\\\\begin{theorem}...\\\\end{theorem}"], ["Abstract", "Paper abstract", "\\\\begin{abstract}...\\\\end{abstract}"], ["Bibliography", "References section", "\\\\begin{thebibliography}..."]], "caption": "Common block types and their LaTeX output"}', 3, 0),
('b1000003-0000-0000-0000-000000000005', 'a1000000-0000-0000-0000-000000000003', 'heading', '{"text": "How Blocks Appear", "level": 2}', 4, 0),
('b1000003-0000-0000-0000-000000000006', 'a1000000-0000-0000-0000-000000000003', 'paragraph', '{"text": "Each block is shown as a **card** in the Studio. The card has a colored badge showing its type (PARAGRAPH, H1, EQUATION, etc.), an edit button, and a 3-dot menu for more options."}', 5, 0),
('b1000003-0000-0000-0000-000000000007', 'a1000000-0000-0000-0000-000000000003', 'paragraph', '{"text": "You can **drag blocks** to reorder them (desktop: drag the grip handle on the left; mobile: long-press and drag). You can also use the **+ button** between blocks to insert a new block at a specific position."}', 6, 0);

-- ============================================================
-- GETTING STARTED 4: Add Your First Blocks
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a1000000-0000-0000-0000-000000000004', 'system', 'Add Your First Blocks', true, 'getting-started', 3, 'add-first-blocks')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a1000000-0000-0000-0000-000000000004';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b1000004-0000-0000-0000-000000000001', 'a1000000-0000-0000-0000-000000000004', 'paragraph', '{"text": "There are three ways to add a block to your document:"}', 0, 0),
('b1000004-0000-0000-0000-000000000002', 'a1000000-0000-0000-0000-000000000004', 'heading', '{"text": "Method 1: The + Button", "level": 2}', 1, 0),
('b1000004-0000-0000-0000-000000000003', 'a1000000-0000-0000-0000-000000000004', 'paragraph', '{"text": "Click **+ Add block** at the bottom of your document, or hover between any two blocks to reveal a small **+** button. Click it to see the block type menu."}', 2, 0),
('b1000004-0000-0000-0000-000000000005', 'a1000000-0000-0000-0000-000000000004', 'heading', '{"text": "Method 2: The / Slash Command", "level": 2}', 3, 0),
('b1000004-0000-0000-0000-000000000006', 'a1000000-0000-0000-0000-000000000004', 'paragraph', '{"text": "In an empty paragraph, type **/** to open the slash command menu. Start typing the block type you want (e.g., /equation, /table, /heading) and select it from the list."}', 4, 0),
('b1000004-0000-0000-0000-000000000007', 'a1000000-0000-0000-0000-000000000004', 'heading', '{"text": "Method 3: The 3-Dot Menu", "level": 2}', 5, 0),
('b1000004-0000-0000-0000-000000000008', 'a1000000-0000-0000-0000-000000000004', 'paragraph', '{"text": "Click the **⋮** menu on any block card and choose **Add block before** or **Add block after** to insert at a specific position."}', 6, 0),
('b1000004-0000-0000-0000-000000000009', 'a1000000-0000-0000-0000-000000000004', 'heading', '{"text": "Try It Now", "level": 2}', 7, 0),
('b1000004-0000-0000-0000-000000000010', 'a1000000-0000-0000-0000-000000000004', 'paragraph', '{"text": "Add a **Heading** block (H1) and type your paper title. Then add a **Paragraph** block below it and write your introduction. That''s it — you''ve started your first document!"}', 8, 0);

-- ============================================================
-- GETTING STARTED 5: Format Your Text
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a1000000-0000-0000-0000-000000000005', 'system', 'Format Your Text', true, 'getting-started', 4, 'format-text')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a1000000-0000-0000-0000-000000000005';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b1000005-0000-0000-0000-000000000001', 'a1000000-0000-0000-0000-000000000005', 'paragraph', '{"text": "Lilia gives you two ways to format text: the **toolbar** (click buttons) or **typing markers** (type symbols). Both produce the same result."}', 0, 0),
('b1000005-0000-0000-0000-000000000002', 'a1000000-0000-0000-0000-000000000005', 'heading', '{"text": "Formatting Reference", "level": 2}', 1, 0),
('b1000005-0000-0000-0000-000000000003', 'a1000000-0000-0000-0000-000000000005', 'table', '{"headers": ["Toolbar Button", "Or Type This", "Result", "LaTeX Output"], "rows": [["B (Bold)", "**text**", "bold text", "\\\\textbf{text}"], ["I (Italic)", "*text*", "italic text", "\\\\textit{text}"], ["U (Underline)", "__text__", "underlined text", "\\\\underline{text}"], ["S (Strikethrough)", "~~text~~", "strikethrough text", "\\\\sout{text}"], ["</> (Code)", "`text`", "monospace text", "\\\\texttt{text}"], ["√ (Math)", "$E=mc^2$", "rendered equation", "$E=mc^2$"]], "caption": "Formatting options — toolbar vs typing"}', 2, 0),
('b1000005-0000-0000-0000-000000000004', 'a1000000-0000-0000-0000-000000000005', 'heading', '{"text": "Using the Toolbar", "level": 2}', 3, 0),
('b1000005-0000-0000-0000-000000000005', 'a1000000-0000-0000-0000-000000000005', 'paragraph', '{"text": "Open a paragraph block in the drawer (click the ✏️ icon). The formatting toolbar appears at the top with buttons for **B**old, *I*talic, __U__nderline, ~~S~~trikethrough, `Code`, and $Math$."}', 4, 0),
('b1000005-0000-0000-0000-000000000006', 'a1000000-0000-0000-0000-000000000005', 'paragraph', '{"text": "Select text, then click a toolbar button to apply formatting. Click again to remove it. The toolbar also has buttons for links (\\\\url{}) and citations (\\\\cite{})."}', 5, 0),
('b1000005-0000-0000-0000-000000000007', 'a1000000-0000-0000-0000-000000000005', 'heading', '{"text": "Typing Markers", "level": 2}', 6, 0),
('b1000005-0000-0000-0000-000000000008', 'a1000000-0000-0000-0000-000000000005', 'paragraph', '{"text": "You can also type formatting markers directly. Lilia''s WYSIWYG editor converts them to rich text as you type. For example, type **hello** and it renders as bold hello."}', 7, 0);

-- ============================================================
-- GETTING STARTED 6: Add Your First Equation
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a1000000-0000-0000-0000-000000000006', 'system', 'Add Your First Equation', true, 'getting-started', 5, 'add-first-equation')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a1000000-0000-0000-0000-000000000006';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b1000006-0000-0000-0000-000000000001', 'a1000000-0000-0000-0000-000000000006', 'paragraph', '{"text": "Math is Lilia''s superpower. There are two ways to add equations:"}', 0, 0),
('b1000006-0000-0000-0000-000000000002', 'a1000000-0000-0000-0000-000000000006', 'heading', '{"text": "Inline Math — Inside a Paragraph", "level": 2}', 1, 0),
('b1000006-0000-0000-0000-000000000003', 'a1000000-0000-0000-0000-000000000006', 'paragraph', '{"text": "Type **dollar signs** around any LaTeX expression inside a paragraph. It renders as math inline with your text."}', 2, 0),
('b1000006-0000-0000-0000-000000000004', 'a1000000-0000-0000-0000-000000000006', 'table', '{"headers": ["You Type", "You See", "LaTeX Output"], "rows": [["$E = mc^2$", "E = mc\u00b2", "$E = mc^2$"], ["$\\\\frac{a}{b}$", "a/b (fraction)", "\\\\frac{a}{b}"], ["$\\\\sqrt{x}$", "\u221ax", "\\\\sqrt{x}"], ["$\\\\alpha, \\\\beta, \\\\theta$", "\u03b1, \u03b2, \u03b8", "\\\\alpha, \\\\beta, \\\\theta"], ["$\\\\sum_{i=1}^{n} x_i$", "\u03a3 x\u1d62", "\\\\sum_{i=1}^{n} x_i"], ["$\\\\int_0^1 f(x)\\\\,dx$", "\u222b f(x) dx", "\\\\int_0^1 f(x)\\\\,dx"]], "caption": "Common inline math expressions"}', 3, 0),
('b1000006-0000-0000-0000-000000000005', 'a1000000-0000-0000-0000-000000000006', 'paragraph', '{"text": "Example: \"The energy-mass equivalence $E = mc^2$ was proposed by Einstein.\""}', 4, 0),
('b1000006-0000-0000-0000-000000000006', 'a1000000-0000-0000-0000-000000000006', 'heading', '{"text": "Display Math — Equation Block", "level": 2}', 5, 0),
('b1000006-0000-0000-0000-000000000007', 'a1000000-0000-0000-0000-000000000006', 'paragraph', '{"text": "For key formulas that deserve their own line, add an **Equation block**. Type the LaTeX directly — no dollar signs needed (the block is already in math mode)."}', 6, 0),
('b1000006-0000-0000-0000-000000000008', 'a1000000-0000-0000-0000-000000000006', 'table', '{"headers": ["When to Use", "Inline $...$", "Equation Block"], "rows": [["Short expressions", "\u2714 Yes", ""], ["Key results / formulas", "", "\u2714 Yes"], ["Needs a number", "", "\u2714 Yes (set numbered)"], ["Needs a label for \\\\ref{}", "", "\u2714 Yes (set label)"], ["Multi-line derivation", "", "\u2714 Yes (align mode)"]], "caption": "When to use inline vs display math"}', 7, 0),
('b1000006-0000-0000-0000-000000000009', 'a1000000-0000-0000-0000-000000000006', 'heading', '{"text": "Try It Now", "level": 2}', 8, 0),
('b1000006-0000-0000-0000-000000000010', 'a1000000-0000-0000-0000-000000000006', 'paragraph', '{"text": "In a paragraph block, type: The function $f(x) = x^2$ is a parabola. Then add an Equation block and type: \\\\frac{d}{dx} x^2 = 2x"}', 9, 0);

-- ============================================================
-- GETTING STARTED 7: Add a Table
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a1000000-0000-0000-0000-000000000007', 'system', 'Add a Table', true, 'getting-started', 6, 'add-a-table')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a1000000-0000-0000-0000-000000000007';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b1000007-0000-0000-0000-000000000001', 'a1000000-0000-0000-0000-000000000007', 'paragraph', '{"text": "Tables are essential for research — results, comparisons, data. Add a **Table** block from the + menu or with /table."}', 0, 0),
('b1000007-0000-0000-0000-000000000002', 'a1000000-0000-0000-0000-000000000007', 'heading', '{"text": "Setting Up a Table", "level": 2}', 1, 0),
('b1000007-0000-0000-0000-000000000003', 'a1000000-0000-0000-0000-000000000007', 'paragraph', '{"text": "A table block has **headers** (column names) and **rows** (data). Click the edit button to open the table editor where you can add/remove columns and rows."}', 2, 0),
('b1000007-0000-0000-0000-000000000004', 'a1000000-0000-0000-0000-000000000007', 'heading', '{"text": "Example: Results Table", "level": 2}', 3, 0),
('b1000007-0000-0000-0000-000000000005', 'a1000000-0000-0000-0000-000000000007', 'table', '{"headers": ["Method", "Accuracy (%)", "F1 Score", "Time (s)"], "rows": [["Baseline", "82.3", "0.79", "12"], ["Our Method", "91.7", "0.89", "18"], ["State-of-art", "93.1", "0.91", "45"]], "caption": "Comparison of methods on benchmark dataset"}', 4, 0),
('b1000007-0000-0000-0000-000000000006', 'a1000000-0000-0000-0000-000000000007', 'heading', '{"text": "LaTeX Output", "level": 2}', 5, 0),
('b1000007-0000-0000-0000-000000000007', 'a1000000-0000-0000-0000-000000000007', 'paragraph', '{"text": "Lilia generates a proper LaTeX table with \\\\begin{table}, \\\\begin{tabular}, \\\\toprule/\\\\midrule/\\\\bottomrule (booktabs style), bold headers, and an optional \\\\caption{} and \\\\label{}."}', 6, 0),
('b1000007-0000-0000-0000-000000000008', 'a1000000-0000-0000-0000-000000000007', 'paragraph', '{"text": "You can use $math$ inside table cells too — e.g., $O(n \\\\log n)$ in a complexity column."}', 7, 0);

-- ============================================================
-- GETTING STARTED 8: Preview & Export
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a1000000-0000-0000-0000-000000000008', 'system', 'Preview & Export', true, 'getting-started', 7, 'preview-and-export')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a1000000-0000-0000-0000-000000000008';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b1000008-0000-0000-0000-000000000001', 'a1000000-0000-0000-0000-000000000008', 'paragraph', '{"text": "You''ve built your document with blocks. Now let''s see what it looks like and export it."}', 0, 0),
('b1000008-0000-0000-0000-000000000002', 'a1000000-0000-0000-0000-000000000008', 'heading', '{"text": "Preview Tab", "level": 2}', 1, 0),
('b1000008-0000-0000-0000-000000000003', 'a1000000-0000-0000-0000-000000000008', 'paragraph', '{"text": "Click **Preview** in the top bar to see your document rendered as it would appear in PDF. Equations are typeset with KaTeX, tables are formatted, and headings create proper structure."}', 2, 0),
('b1000008-0000-0000-0000-000000000004', 'a1000000-0000-0000-0000-000000000008', 'heading', '{"text": "Validate Tab", "level": 2}', 3, 0),
('b1000008-0000-0000-0000-000000000005', 'a1000000-0000-0000-0000-000000000008', 'paragraph', '{"text": "Click **Validate** to check your LaTeX for errors. Lilia compiles each block and highlights any issues — like mismatched brackets, undefined commands, or missing packages. Click the error to see a suggested fix."}', 4, 0),
('b1000008-0000-0000-0000-000000000006', 'a1000000-0000-0000-0000-000000000008', 'heading', '{"text": "Export Options", "level": 2}', 5, 0),
('b1000008-0000-0000-0000-000000000007', 'a1000000-0000-0000-0000-000000000008', 'table', '{"headers": ["Format", "What You Get", "Best For"], "rows": [["LaTeX (.tex)", "Single .tex file with full preamble", "Quick export, paste into Overleaf"], ["ZIP", ".tex + images + .bib file", "Complete project, upload to Overleaf"], ["PDF", "Compiled PDF document", "Sharing with non-LaTeX users"], ["Multi-file", "Separate files per chapter", "Theses and long documents"]], "caption": "Export formats"}', 6, 0),
('b1000008-0000-0000-0000-000000000008', 'a1000000-0000-0000-0000-000000000008', 'heading', '{"text": "What Happens When You Export", "level": 2}', 7, 0),
('b1000008-0000-0000-0000-000000000009', 'a1000000-0000-0000-0000-000000000008', 'paragraph', '{"text": "Lilia converts your blocks to a complete LaTeX document with: \\\\documentclass, packages (amsmath, graphicx, booktabs, hyperref, etc.), \\\\title, \\\\maketitle, sections, and all content. The output compiles directly with pdflatex — no manual editing needed."}', 8, 0),
('b1000008-0000-0000-0000-000000000010', 'a1000000-0000-0000-0000-000000000008', 'heading', '{"text": "Next Steps", "level": 2}', 9, 0),
('b1000008-0000-0000-0000-000000000011', 'a1000000-0000-0000-0000-000000000008', 'paragraph', '{"text": "You''ve completed the Getting Started guide! You now know how to create documents, add blocks, format text, write equations, build tables, and export. Explore the **Syntax** guides for inline math and citations, or the **Reference** section for detailed block documentation."}', 10, 0);
