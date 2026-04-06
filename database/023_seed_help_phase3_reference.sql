-- Phase 3: Block Type → LaTeX Reference articles
-- Detailed reference for each block type showing content fields, options, and exact LaTeX output

-- ============================================================
-- REF: Paragraph Block
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a3000000-0000-0000-0000-000000000001', 'system', 'Paragraph Block Reference', true, 'reference', 10, 'ref-paragraph')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a3000000-0000-0000-0000-000000000001';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b3010000-0000-0000-0000-000000000001', 'a3000000-0000-0000-0000-000000000001', 'paragraph', '{"text": "The paragraph block is the default text block. It supports rich formatting, inline math, citations, and cross-references."}', 0, 0),
('b3010000-0000-0000-0000-000000000002', 'a3000000-0000-0000-0000-000000000001', 'heading', '{"text": "Content Fields", "level": 2}', 1, 0),
('b3010000-0000-0000-0000-000000000003', 'a3000000-0000-0000-0000-000000000001', 'table', '{"headers": ["Field", "Type", "Required", "Description"], "rows": [["text", "string", "Yes", "The paragraph text with inline formatting markers"]], "caption": ""}', 2, 0),
('b3010000-0000-0000-0000-000000000004', 'a3000000-0000-0000-0000-000000000001', 'heading', '{"text": "Inline Formatting → LaTeX", "level": 2}', 3, 0),
('b3010000-0000-0000-0000-000000000005', 'a3000000-0000-0000-0000-000000000001', 'table', '{"headers": ["Marker", "LaTeX Output", "Example"], "rows": [["**text**", "\\\\textbf{text}", "**bold** → \\\\textbf{bold}"], ["*text*", "\\\\textit{text}", "*italic* → \\\\textit{italic}"], ["__text__", "\\\\underline{text}", "__underline__ → \\\\underline{underline}"], ["~~text~~", "\\\\sout{text}", "~~strike~~ → \\\\sout{strike}"], ["`text`", "\\\\texttt{text}", "`code` → \\\\texttt{code}"], ["$E=mc^2$", "$E=mc^2$", "Inline math preserved as-is"], ["\\\\cite{key}", "\\\\cite{key}", "Citation reference"], ["\\\\ref{label}", "\\\\ref{label}", "Cross-reference"], ["\\\\url{url}", "\\\\url{url}", "URL link"], ["\\\\footnote{text}", "\\\\footnote{text}", "Footnote"]], "caption": "Inline formatting to LaTeX mapping"}', 4, 0),
('b3010000-0000-0000-0000-000000000006', 'a3000000-0000-0000-0000-000000000001', 'heading', '{"text": "API Schema", "level": 2}', 5, 0),
('b3010000-0000-0000-0000-000000000007', 'a3000000-0000-0000-0000-000000000001', 'code', '{"code": "{\n  \"type\": \"paragraph\",\n  \"content\": {\n    \"text\": \"The function $f(x) = x^2$ is **convex** \\\\cite{boyd2004}.\"\n  },\n  \"sortOrder\": 5\n}", "language": "json"}', 6, 0);

-- ============================================================
-- REF: Heading Block
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a3000000-0000-0000-0000-000000000002', 'system', 'Heading Block Reference', true, 'reference', 11, 'ref-heading')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a3000000-0000-0000-0000-000000000002';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b3020000-0000-0000-0000-000000000001', 'a3000000-0000-0000-0000-000000000002', 'paragraph', '{"text": "Creates section structure in your document. Each level maps to a specific LaTeX sectioning command."}', 0, 0),
('b3020000-0000-0000-0000-000000000002', 'a3000000-0000-0000-0000-000000000002', 'heading', '{"text": "Content Fields", "level": 2}', 1, 0),
('b3020000-0000-0000-0000-000000000003', 'a3000000-0000-0000-0000-000000000002', 'table', '{"headers": ["Field", "Type", "Required", "Default", "Description"], "rows": [["text", "string", "Yes", "\"\"", "The heading text"], ["level", "number", "No", "1", "Heading level (1-6)"]], "caption": ""}', 2, 0),
('b3020000-0000-0000-0000-000000000004', 'a3000000-0000-0000-0000-000000000002', 'heading', '{"text": "Level → LaTeX Mapping", "level": 2}', 3, 0),
('b3020000-0000-0000-0000-000000000005', 'a3000000-0000-0000-0000-000000000002', 'table', '{"headers": ["Level", "LaTeX Command", "Typical Use"], "rows": [["1", "\\\\section{text}", "Document title, chapter title"], ["2", "\\\\subsection{text}", "Major sections (Introduction, Methods)"], ["3", "\\\\subsubsection{text}", "Subsections"], ["4", "\\\\paragraph{text}", "Minor subdivisions"], ["5-6", "\\\\paragraph{text}", "Fine-grained structure"]], "caption": "Heading levels and LaTeX commands"}', 4, 0),
('b3020000-0000-0000-0000-000000000006', 'a3000000-0000-0000-0000-000000000002', 'heading', '{"text": "API Schema", "level": 2}', 5, 0),
('b3020000-0000-0000-0000-000000000007', 'a3000000-0000-0000-0000-000000000002', 'code', '{"code": "{\n  \"type\": \"heading\",\n  \"content\": {\n    \"text\": \"3. Results\",\n    \"level\": 2\n  },\n  \"sortOrder\": 10\n}", "language": "json"}', 6, 0);

-- ============================================================
-- REF: Equation Block
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a3000000-0000-0000-0000-000000000003', 'system', 'Equation Block Reference', true, 'reference', 12, 'ref-equation')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a3000000-0000-0000-0000-000000000003';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b3030000-0000-0000-0000-000000000001', 'a3000000-0000-0000-0000-000000000003', 'paragraph', '{"text": "Renders LaTeX math as display equations. Supports numbered equations, labels for cross-referencing, and multi-line modes."}', 0, 0),
('b3030000-0000-0000-0000-000000000002', 'a3000000-0000-0000-0000-000000000003', 'heading', '{"text": "Content Fields", "level": 2}', 1, 0),
('b3030000-0000-0000-0000-000000000003', 'a3000000-0000-0000-0000-000000000003', 'table', '{"headers": ["Field", "Type", "Required", "Default", "Description"], "rows": [["latex", "string", "Yes", "\"\"", "LaTeX math content (no $ needed)"], ["displayMode", "boolean", "No", "true", "Display (centered) vs inline"], ["equationMode", "string", "No", "\"display\"", "display, align, or gather"], ["numbered", "boolean", "No", "true", "Show equation number"], ["label", "string", "No", "\"\"", "Label for \\\\ref{} cross-reference"]], "caption": ""}', 2, 0),
('b3030000-0000-0000-0000-000000000004', 'a3000000-0000-0000-0000-000000000003', 'heading', '{"text": "Mode → LaTeX Mapping", "level": 2}', 3, 0),
('b3030000-0000-0000-0000-000000000005', 'a3000000-0000-0000-0000-000000000003', 'table', '{"headers": ["Mode", "Numbered", "LaTeX Environment"], "rows": [["display", "true", "\\\\begin{equation}\\\\label{label}...\\\\end{equation}"], ["display", "false", "\\\\begin{equation*}...\\\\end{equation*}"], ["align", "true", "\\\\begin{align}\\\\label{label}...\\\\end{align}"], ["align", "false", "\\\\begin{align*}...\\\\end{align*}"], ["gather", "true", "\\\\begin{gather}\\\\label{label}...\\\\end{gather}"], ["gather", "false", "\\\\begin{gather*}...\\\\end{gather*}"]], "caption": "All equation mode and numbering combinations"}', 4, 0),
('b3030000-0000-0000-0000-000000000006', 'a3000000-0000-0000-0000-000000000003', 'heading', '{"text": "API Schema Examples", "level": 2}', 5, 0),
('b3030000-0000-0000-0000-000000000007', 'a3000000-0000-0000-0000-000000000003', 'code', '{"code": "// Numbered equation with label\n{\n  \"type\": \"equation\",\n  \"content\": {\n    \"latex\": \"E = mc^2\",\n    \"numbered\": true,\n    \"label\": \"eq:einstein\"\n  }\n}\n\n// Multi-line aligned\n{\n  \"type\": \"equation\",\n  \"content\": {\n    \"latex\": \"a &= b + c \\\\\\\\\\\\\\\\ d &= e + f\",\n    \"equationMode\": \"align\",\n    \"numbered\": true\n  }\n}\n\n// Unnumbered display\n{\n  \"type\": \"equation\",\n  \"content\": {\n    \"latex\": \"\\\\\\\\int_0^1 f(x)\\\\\\\\,dx\",\n    \"numbered\": false\n  }\n}", "language": "json"}', 6, 0);

-- ============================================================
-- REF: Table Block
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a3000000-0000-0000-0000-000000000004', 'system', 'Table Block Reference', true, 'reference', 13, 'ref-table')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a3000000-0000-0000-0000-000000000004';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b3040000-0000-0000-0000-000000000001', 'a3000000-0000-0000-0000-000000000004', 'paragraph', '{"text": "Data tables with headers, rows, optional caption and label. Generates booktabs-style LaTeX tables."}', 0, 0),
('b3040000-0000-0000-0000-000000000002', 'a3000000-0000-0000-0000-000000000004', 'heading', '{"text": "Content Fields", "level": 2}', 1, 0),
('b3040000-0000-0000-0000-000000000003', 'a3000000-0000-0000-0000-000000000004', 'table', '{"headers": ["Field", "Type", "Required", "Default", "Description"], "rows": [["headers", "string[]", "Yes", "[]", "Column header names"], ["rows", "string[][]", "Yes", "[]", "2D array of cell values"], ["columnAlign", "string[]", "No", "[]", "Per-column alignment: left, center, right"], ["caption", "string", "No", "\"\"", "Table caption text"], ["label", "string", "No", "\"\"", "Label for \\\\ref{tbl:name}"]], "caption": ""}', 2, 0),
('b3040000-0000-0000-0000-000000000004', 'a3000000-0000-0000-0000-000000000004', 'heading', '{"text": "LaTeX Output", "level": 2}', 3, 0),
('b3040000-0000-0000-0000-000000000005', 'a3000000-0000-0000-0000-000000000004', 'code', '{"code": "\\\\begin{table}[H]\n\\\\centering\n\\\\renewcommand{\\\\arraystretch}{1.3}\n\\\\caption{Comparison of methods}\\\\label{tbl:results}\n\\\\begin{tabular}{lcc}\n\\\\toprule\n\\\\textbf{Method} & \\\\textbf{Accuracy} & \\\\textbf{Time} \\\\\\\\\n\\\\midrule\nBaseline & 82.3\\\\% & 12s \\\\\\\\\nOurs     & 91.7\\\\% & 18s \\\\\\\\\n\\\\bottomrule\n\\\\end{tabular}\n\\\\end{table}", "language": "latex"}', 4, 0),
('b3040000-0000-0000-0000-000000000006', 'a3000000-0000-0000-0000-000000000004', 'paragraph', '{"text": "Tables use **booktabs** style (\\\\toprule, \\\\midrule, \\\\bottomrule) for professional appearance. Cell content supports $math$ inline."}', 5, 0);

-- ============================================================
-- REF: Theorem Block
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a3000000-0000-0000-0000-000000000005', 'system', 'Theorem Block Reference', true, 'reference', 14, 'ref-theorem')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a3000000-0000-0000-0000-000000000005';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b3050000-0000-0000-0000-000000000001', 'a3000000-0000-0000-0000-000000000005', 'paragraph', '{"text": "Supports 8 mathematical environments. Each maps to a LaTeX theorem-family command."}', 0, 0),
('b3050000-0000-0000-0000-000000000002', 'a3000000-0000-0000-0000-000000000005', 'heading', '{"text": "Content Fields", "level": 2}', 1, 0),
('b3050000-0000-0000-0000-000000000003', 'a3000000-0000-0000-0000-000000000005', 'table', '{"headers": ["Field", "Type", "Required", "Default", "Description"], "rows": [["theoremType", "string", "Yes", "\"theorem\"", "Environment type (see table below)"], ["title", "string", "No", "\"\"", "Optional title shown in [brackets]"], ["text", "string", "Yes", "\"\"", "Statement or proof text (supports $math$)"], ["label", "string", "No", "\"\"", "Label for \\\\ref{thm:name}"]], "caption": ""}', 2, 0),
('b3050000-0000-0000-0000-000000000004', 'a3000000-0000-0000-0000-000000000005', 'heading', '{"text": "Type → LaTeX Environment", "level": 2}', 3, 0),
('b3050000-0000-0000-0000-000000000005', 'a3000000-0000-0000-0000-000000000005', 'table', '{"headers": ["theoremType", "LaTeX Environment", "Numbered", "QED Symbol"], "rows": [["theorem", "\\\\begin{theorem}", "Yes", "No"], ["lemma", "\\\\begin{lemma}", "Yes", "No"], ["proposition", "\\\\begin{proposition}", "Yes", "No"], ["corollary", "\\\\begin{corollary}", "Yes", "No"], ["definition", "\\\\begin{definition}", "Yes", "No"], ["example", "\\\\begin{example}", "Yes", "No"], ["remark", "\\\\begin{remark}", "Yes", "No"], ["proof", "\\\\begin{proof}", "No", "Yes (\\u25a1)"]], "caption": "All theorem types and their LaTeX environments"}', 4, 0),
('b3050000-0000-0000-0000-000000000006', 'a3000000-0000-0000-0000-000000000005', 'heading', '{"text": "LaTeX Output Example", "level": 2}', 5, 0),
('b3050000-0000-0000-0000-000000000007', 'a3000000-0000-0000-0000-000000000005', 'code', '{"code": "% With title and label:\n\\\\begin{theorem}[Universal Approximation]\\\\label{thm:approx}\nFor any continuous $f$ on a compact set and\n$\\\\varepsilon > 0$, there exists a network $g$\nwith $\\\\|f - g\\\\|_\\\\infty < \\\\varepsilon$.\n\\\\end{theorem}\n\n% Proof (auto QED):\n\\\\begin{proof}\nBy construction...\n\\\\end{proof}", "language": "latex"}', 6, 0);

-- ============================================================
-- REF: Figure Block
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a3000000-0000-0000-0000-000000000006', 'system', 'Figure Block Reference', true, 'reference', 15, 'ref-figure')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a3000000-0000-0000-0000-000000000006';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b3060000-0000-0000-0000-000000000001', 'a3000000-0000-0000-0000-000000000006', 'paragraph', '{"text": "Images with caption, sizing, and label for cross-referencing."}', 0, 0),
('b3060000-0000-0000-0000-000000000002', 'a3000000-0000-0000-0000-000000000006', 'table', '{"headers": ["Field", "Type", "Default", "LaTeX Effect"], "rows": [["src", "string (URL)", "required", "\\\\includegraphics{filename}"], ["caption", "string", "\"\"", "\\\\caption{text}"], ["alt", "string", "\"\"", "Alt text (accessibility, not in LaTeX)"], ["width", "number (0-1)", "0.8", "width=0.8\\\\textwidth"], ["label", "string", "\"\"", "\\\\label{fig:name}"], ["placement", "string", "\"auto\"", "[H] or [htbp] float placement"]], "caption": "Figure block content fields"}', 1, 0),
('b3060000-0000-0000-0000-000000000003', 'a3000000-0000-0000-0000-000000000006', 'code', '{"code": "\\\\begin{figure}[H]\n\\\\centering\n\\\\includegraphics[width=0.8\\\\textwidth]{figures/architecture.png}\n\\\\caption{System architecture overview}\n\\\\label{fig:architecture}\n\\\\end{figure}", "language": "latex"}', 2, 0);

-- ============================================================
-- REF: Code Block
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a3000000-0000-0000-0000-000000000007', 'system', 'Code Block Reference', true, 'reference', 16, 'ref-code')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a3000000-0000-0000-0000-000000000007';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b3070000-0000-0000-0000-000000000001', 'a3000000-0000-0000-0000-000000000007', 'paragraph', '{"text": "Syntax-highlighted source code. Supports 20+ languages."}', 0, 0),
('b3070000-0000-0000-0000-000000000002', 'a3000000-0000-0000-0000-000000000007', 'table', '{"headers": ["Field", "Type", "Default", "LaTeX Effect"], "rows": [["code", "string", "required", "Content inside lstlisting"], ["language", "string", "\"\"", "language= option in lstlisting"]], "caption": "Code block content fields"}', 1, 0),
('b3070000-0000-0000-0000-000000000003', 'a3000000-0000-0000-0000-000000000007', 'paragraph', '{"text": "**Supported languages:** Python, JavaScript, TypeScript, Java, C, C++, C#, Go, Rust, Ruby, PHP, SQL, R, MATLAB, LaTeX, HTML, CSS, Bash, Pseudocode."}', 2, 0),
('b3070000-0000-0000-0000-000000000004', 'a3000000-0000-0000-0000-000000000007', 'code', '{"code": "\\\\begin{lstlisting}[language=python]\ndef fibonacci(n):\n    if n <= 1:\n        return n\n    return fibonacci(n-1) + fibonacci(n-2)\n\\\\end{lstlisting}", "language": "latex"}', 3, 0);

-- ============================================================
-- REF: List Block
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a3000000-0000-0000-0000-000000000008', 'system', 'List Block Reference', true, 'reference', 17, 'ref-list')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a3000000-0000-0000-0000-000000000008';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b3080000-0000-0000-0000-000000000001', 'a3000000-0000-0000-0000-000000000008', 'paragraph', '{"text": "Bullet or numbered lists. Items support inline formatting and $math$."}', 0, 0),
('b3080000-0000-0000-0000-000000000002', 'a3000000-0000-0000-0000-000000000008', 'table', '{"headers": ["Field", "Type", "Default", "LaTeX Effect"], "rows": [["items", "string[]", "[]", "Each item becomes \\\\item"], ["ordered", "boolean", "false", "itemize (false) or enumerate (true)"], ["start", "number", "1", "start= option for enumerate"]], "caption": "List block content fields"}', 1, 0),
('b3080000-0000-0000-0000-000000000003', 'a3000000-0000-0000-0000-000000000008', 'code', '{"code": "% Unordered:\n\\\\begin{itemize}\n\\\\item First item with **bold**\n\\\\item Second with $math$\n\\\\end{itemize}\n\n% Ordered:\n\\\\begin{enumerate}\n\\\\item Step one\n\\\\item Step two\n\\\\end{enumerate}", "language": "latex"}', 2, 0);

-- ============================================================
-- REF: Abstract Block
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a3000000-0000-0000-0000-000000000009', 'system', 'Abstract Block Reference', true, 'reference', 18, 'ref-abstract')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a3000000-0000-0000-0000-000000000009';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b3090000-0000-0000-0000-000000000001', 'a3000000-0000-0000-0000-000000000009', 'paragraph', '{"text": "Paper abstract section. Supports inline formatting and $math$."}', 0, 0),
('b3090000-0000-0000-0000-000000000002', 'a3000000-0000-0000-0000-000000000009', 'table', '{"headers": ["Field", "Type", "Required", "LaTeX Effect"], "rows": [["text", "string", "Yes", "Content inside \\\\begin{abstract}...\\\\end{abstract}"]], "caption": ""}', 1, 0),
('b3090000-0000-0000-0000-000000000009', 'a3000000-0000-0000-0000-000000000009', 'code', '{"code": "\\\\begin{abstract}\nWe analyze the convergence of SGD with\nmomentum for non-convex optimization.\n\\\\end{abstract}", "language": "latex"}', 2, 0);

-- ============================================================
-- REF: Bibliography Block
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a3000000-0000-0000-0000-000000000010', 'system', 'Bibliography Block Reference', true, 'reference', 19, 'ref-bibliography')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a3000000-0000-0000-0000-000000000010';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b3100000-0000-0000-0000-000000000001', 'a3000000-0000-0000-0000-000000000010', 'paragraph', '{"text": "References section with DOI/ISBN auto-lookup. Entries are cited in text with \\\\cite{key}."}', 0, 0),
('b3100000-0000-0000-0000-000000000002', 'a3000000-0000-0000-0000-000000000010', 'table', '{"headers": ["Field", "Type", "Description"], "rows": [["style", "string", "Citation style: numeric, apa, authoryear, chicago"], ["entries[]", "object[]", "Array of bibliography entries"], ["entries[].citeKey", "string", "Unique key for \\\\cite{key}"], ["entries[].entryType", "string", "article, book, inproceedings, etc."], ["entries[].author", "string", "Author name(s)"], ["entries[].title", "string", "Publication title"], ["entries[].year", "string", "Publication year"], ["entries[].journal", "string", "Journal name (articles)"], ["entries[].doi", "string", "DOI identifier"]], "caption": "Bibliography block content fields"}', 1, 0),
('b3100000-0000-0000-0000-000000000003', 'a3000000-0000-0000-0000-000000000010', 'code', '{"code": "\\\\begin{thebibliography}{99}\n\\\\bibitem{robbins1951} H.~Robbins and S.~Monro (1951).\n\\\\textit{A Stochastic Approximation Method}.\nThe Annals of Mathematical Statistics, 22(3), 400--407.\n\\\\end{thebibliography}", "language": "latex"}', 2, 0);

-- ============================================================
-- REF: LaTeX Preamble & Packages
-- ============================================================
INSERT INTO documents (id, owner_id, title, is_help_content, help_category, help_order, help_slug)
VALUES ('a3000000-0000-0000-0000-000000000011', 'system', 'LaTeX Preamble & Packages', true, 'reference', 20, 'ref-latex-preamble')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, help_order = EXCLUDED.help_order;

DELETE FROM blocks WHERE document_id = 'a3000000-0000-0000-0000-000000000011';
INSERT INTO blocks (id, document_id, type, content, sort_order, depth) VALUES
('b3110000-0000-0000-0000-000000000001', 'a3000000-0000-0000-0000-000000000011', 'paragraph', '{"text": "When you export a document, Lilia generates a complete LaTeX preamble with all necessary packages. Here''s what''s included:"}', 0, 0),
('b3110000-0000-0000-0000-000000000002', 'a3000000-0000-0000-0000-000000000011', 'heading', '{"text": "Core Packages", "level": 2}', 1, 0),
('b3110000-0000-0000-0000-000000000003', 'a3000000-0000-0000-0000-000000000011', 'table', '{"headers": ["Package", "Purpose"], "rows": [["inputenc (utf8)", "UTF-8 character encoding"], ["fontenc (T1)", "Font encoding"], ["amsmath, amssymb, amsfonts, amsthm", "Math environments and symbols"], ["mathtools", "Extended math tools (dcases, etc.)"], ["graphicx", "Image inclusion (\\\\includegraphics)"], ["float", "Float placement ([H] option)"], ["booktabs", "Professional table rules (\\\\toprule, \\\\midrule)"], ["hyperref", "Clickable links and cross-references"], ["listings", "Code block syntax highlighting"], ["enumitem", "Customizable list formatting"], ["xcolor", "Color support (\\\\textcolor, \\\\hl)"], ["microtype", "Typography improvements"]], "caption": "Packages included in every export"}', 2, 0),
('b3110000-0000-0000-0000-000000000004', 'a3000000-0000-0000-0000-000000000011', 'heading', '{"text": "Document Class Options", "level": 2}', 3, 0),
('b3110000-0000-0000-0000-000000000005', 'a3000000-0000-0000-0000-000000000011', 'table', '{"headers": ["Setting", "Values", "LaTeX Effect"], "rows": [["documentClass", "article (default), report, book", "\\\\documentclass{article}"], ["fontSize", "10, 11, 12", "\\\\documentclass[11pt]"], ["paperSize", "a4, letter", "\\\\documentclass[a4paper]"], ["fontFamily", "serif (default), charter, times", "Font packages"]], "caption": "Document class options"}', 4, 0),
('b3110000-0000-0000-0000-000000000006', 'a3000000-0000-0000-0000-000000000011', 'heading', '{"text": "Theorem Environments", "level": 2}', 5, 0),
('b3110000-0000-0000-0000-000000000007', 'a3000000-0000-0000-0000-000000000011', 'paragraph', '{"text": "The preamble auto-includes \\\\newtheorem declarations for: theorem, lemma, proposition, corollary, definition, example, remark, claim, assumption, axiom, conjecture, hypothesis — plus unnumbered (\\*) variants for all."}', 6, 0);

-- Update search_text for all new articles
UPDATE documents d SET search_text = (
  SELECT d.title || ' ' || COALESCE(string_agg(b.content->>'text', ' '), '')
  FROM blocks b WHERE b.document_id = d.id
)
WHERE d.is_help_content = true AND d.search_text IS NULL;
