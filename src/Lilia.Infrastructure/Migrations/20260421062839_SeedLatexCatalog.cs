using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedLatexCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // v1 seed for the LaTeX catalog. Idempotent — ON CONFLICT DO NOTHING
            // everywhere so rerunning against a populated DB is a no-op. Scope
            // is driven by real prod data (7-day aggregate of import_diagnostics)
            // plus the kernel essentials for every supported class.

            // =============================================================
            //  Document classes
            // =============================================================
            migrationBuilder.Sql(@"
INSERT INTO latex_document_classes (slug, display_name, category, coverage_level, default_engine, required_packages, shim_name, notes) VALUES
('article',       'Article',       'article',      'full',     'pdflatex', '[]'::jsonb, NULL,        'Base class — fully supported'),
('report',        'Report',        'report',       'full',     'pdflatex', '[]'::jsonb, NULL,        'Chapters + sections; same support as article'),
('book',          'Book',          'book',         'partial',  'pdflatex', '[]'::jsonb, NULL,        'Parts / chapters supported; front/back matter partial'),
('memoir',        'Memoir',        'memoir',       'partial',  'pdflatex', '[]'::jsonb, NULL,        'Book-like with advanced layout — partial'),
('beamer',        'Beamer',        'presentation', 'shimmed',  'pdflatex', '[]'::jsonb, 'beamer',    'Shimmed to slide blocks; frame → slide'),
('letter',        'Letter',        'letter',       'partial',  'pdflatex', '[]'::jsonb, NULL,        'letter env parsed as structural block'),
('moderncv',      'ModernCV',      'cv',           'shimmed',  'pdflatex', '[]'::jsonb, 'moderncv',  'Class-aware shim — CV entries mapped to cvEntry blocks'),
('altacv',        'AltaCV',        'cv',           'shimmed',  'xelatex',  '[]'::jsonb, 'altacv',    'Class-aware shim — two-column CV'),
('resume',        'Resume',        'cv',           'shimmed',  'pdflatex', '[]'::jsonb, 'resume',    'Generic resume shim — rSection environment'),
('IEEEtran',      'IEEEtran',      'article',      'partial',  'pdflatex', '[]'::jsonb, NULL,        'IEEE paper class; author/affiliation blocks partial'),
('acmart',        'acmart',        'article',      'partial',  'pdflatex', '[]'::jsonb, NULL,        'ACM paper class; CCS concepts partial'),
('exam',          'exam',          'article',      'partial',  'pdflatex', '[]'::jsonb, NULL,        'Question / problem / solution environments'),
('tufte-book',    'Tufte Book',    'book',         'partial',  'pdflatex', '[]'::jsonb, NULL,        'Margin notes / side floats — partial')
ON CONFLICT (slug) DO NOTHING;
");

            // =============================================================
            //  Packages — top ~45 seen in the wild + common kernel deps
            // =============================================================
            migrationBuilder.Sql(@"
INSERT INTO latex_packages (slug, display_name, category, coverage_level, coverage_notes, ctan_url) VALUES
('amsmath',         'amsmath',         'math',         'full',       'Core math environments — aligned / equation / gather fully mapped', 'https://ctan.org/pkg/amsmath'),
('amssymb',         'amssymb',         'math',         'full',       'Symbol set rendered via KaTeX', 'https://ctan.org/pkg/amssymb'),
('amsthm',          'amsthm',          'math',         'shimmed',    'theorem / definition / proof envs mapped to theorem block', 'https://ctan.org/pkg/amsthm'),
('mathtools',       'mathtools',       'math',         'partial',    'amsmath superset — most envs work, some advanced spacing lost', 'https://ctan.org/pkg/mathtools'),
('bm',              'bm',              'math',         'full',       'Bold math rendered via mathbf fallback', 'https://ctan.org/pkg/bm'),
('unicode-math',    'unicode-math',    'math',         'partial',    'OpenType math — XeLaTeX/LuaLaTeX only', 'https://ctan.org/pkg/unicode-math'),
('graphicx',        'graphicx',        'graphics',     'full',       'includegraphics → figure block', 'https://ctan.org/pkg/graphicx'),
('tikz',            'tikz',            'graphics',     'partial',    'Raw passthrough — figures preserved but not re-rendered as blocks', 'https://ctan.org/pkg/pgf'),
('pgfplots',        'pgfplots',        'graphics',     'none',       'Plot code preserved as raw LaTeX only', 'https://ctan.org/pkg/pgfplots'),
('float',           'float',           'graphics',     'full',       'Float placement options honored', 'https://ctan.org/pkg/float'),
('subfigure',       'subfigure',       'graphics',     'partial',    'Subfigures collapsed to a single figure with caption', 'https://ctan.org/pkg/subfigure'),
('subcaption',      'subcaption',      'graphics',     'partial',    'Subcaptions merged into parent figure caption', 'https://ctan.org/pkg/caption'),
('wrapfig',         'wrapfig',         'graphics',     'partial',    'wrapfigure preserved as figure block; text wrap not rendered', 'https://ctan.org/pkg/wrapfig'),
('array',           'array',           'table',        'full',       'Column types (m, p, b) honored', 'https://ctan.org/pkg/array'),
('tabularx',        'tabularx',        'table',        'partial',    'Variable-width columns flattened to tabular on import', 'https://ctan.org/pkg/tabularx'),
('longtable',       'longtable',       'table',        'partial',    'Page-breaking table → multi-page table block', 'https://ctan.org/pkg/longtable'),
('booktabs',        'booktabs',        'table',        'full',       'toprule / midrule / bottomrule mapped to table rules', 'https://ctan.org/pkg/booktabs'),
('multirow',        'multirow',        'table',        'partial',    'Row-spanning cells preserved on export only', 'https://ctan.org/pkg/multirow'),
('biblatex',        'biblatex',        'bibliography', 'full',       'cite family + printbibliography mapped to citation pills + bibliography block', 'https://ctan.org/pkg/biblatex'),
('natbib',          'natbib',          'bibliography', 'full',       'citep / citet / citealp mapped to citation pills', 'https://ctan.org/pkg/natbib'),
('geometry',        'geometry',        'layout',       'full',       'margin / paper-size options read into Document columns', 'https://ctan.org/pkg/geometry'),
('fancyhdr',        'fancyhdr',        'layout',       'partial',    'Header / footer commands preserved but not re-rendered in preview', 'https://ctan.org/pkg/fancyhdr'),
('setspace',        'setspace',        'layout',       'full',       'singlespacing / onehalfspacing / doublespacing honored', 'https://ctan.org/pkg/setspace'),
('multicol',        'multicol',        'layout',       'partial',    'multicols preserved as raw passthrough — columns not rendered as blocks', 'https://ctan.org/pkg/multicol'),
('titlesec',        'titlesec',        'layout',       'none',       'Custom section formatting preserved for export; not re-rendered', 'https://ctan.org/pkg/titlesec'),
('paracol',         'paracol',         'layout',       'partial',    'Parallel columns preserved as raw passthrough', 'https://ctan.org/pkg/paracol'),
('babel',           'babel',           'language',     'full',       'Language set read into Document.Language', 'https://ctan.org/pkg/babel'),
('polyglossia',     'polyglossia',     'language',     'full',       'XeLaTeX/LuaLaTeX language setup read similarly', 'https://ctan.org/pkg/polyglossia'),
('inputenc',        'inputenc',        'language',     'full',       'utf8 default assumed — non-utf8 inputs converted', 'https://ctan.org/pkg/inputenc'),
('fontenc',         'fontenc',         'language',     'full',       'T1 / LGR / T2A accepted', 'https://ctan.org/pkg/fontenc'),
('csquotes',        'csquotes',        'language',     'partial',    'enquote rendered as regular quotes', 'https://ctan.org/pkg/csquotes'),
('fontspec',        'fontspec',        'font',         'partial',    'XeLaTeX/LuaLaTeX fontspec honored; engine forced to xelatex', 'https://ctan.org/pkg/fontspec'),
('xltxtra',         'xltxtra',         'font',         'partial',    'XeLaTeX helpers', 'https://ctan.org/pkg/xltxtra'),
('microtype',       'microtype',       'font',         'full',       'Typographic adjustments — no-op in preview, preserved on export', 'https://ctan.org/pkg/microtype'),
('listings',        'listings',        'code',         'full',       'lstlisting → code block with language', 'https://ctan.org/pkg/listings'),
('minted',          'minted',          'code',         'partial',    'Preserved as raw passthrough; pygments deps not available at import', 'https://ctan.org/pkg/minted'),
('verbatim',        'verbatim',        'code',         'full',       'verbatim / Verbatim → code block', 'https://ctan.org/pkg/verbatim'),
('hyperref',        'hyperref',        'reference',    'full',       'url / href / ref / autoref all handled', 'https://ctan.org/pkg/hyperref'),
('cleveref',        'cleveref',        'reference',    'full',       'cref / Cref replacements', 'https://ctan.org/pkg/cleveref'),
('xcolor',          'xcolor',          'utility',      'partial',    'Named colors honored; custom models not supported', 'https://ctan.org/pkg/xcolor'),
('enumitem',        'enumitem',        'utility',      'partial',    'Customised lists partially supported', 'https://ctan.org/pkg/enumitem'),
('siunitx',         'siunitx',         'utility',      'partial',    'SI and friends rendered as inline text', 'https://ctan.org/pkg/siunitx'),
('caption',         'caption',         'utility',      'full',       'Custom caption formatting reduced to default style', 'https://ctan.org/pkg/caption'),
('tcolorbox',       'tcolorbox',       'utility',      'partial',    'Coloured boxes preserved as raw passthrough', 'https://ctan.org/pkg/tcolorbox'),
('etoolbox',        'etoolbox',        'utility',      'full',       'Programming helpers — preserved verbatim', 'https://ctan.org/pkg/etoolbox'),
('pdfpages',        'pdfpages',        'utility',      'none',       'Included PDF pages preserved for re-export only', 'https://ctan.org/pkg/pdfpages'),
('appendix',        'appendix',        'utility',      'partial',    'appendices environment preserved as a section', 'https://ctan.org/pkg/appendix'),
('calendar',        'calendar',        'utility',      'none',       'Calendar typesetting preserved verbatim', 'https://ctan.org/pkg/calendar'),
-- Class-packages: tokens reference these as package_slug so they must exist as packages too.
-- LaTeX-wise this is legit — e.g. \documentclass{beamer} OR \usepackage{beamer}. Keep the
-- class rows authoritative for class-specific metadata; duplicate here purely for the FK.
('beamer',          'beamer',          'presentation', 'shimmed',    'Class-package — frame / block / titlepage tokens attach here', 'https://ctan.org/pkg/beamer'),
('moderncv',        'moderncv',        'cv',           'shimmed',    'Class-package — CV command family attaches here', 'https://ctan.org/pkg/moderncv'),
('tufte-book',      'tufte-book',      'layout',       'partial',    'Class-package — marginfigure / fullwidth / twothirdswidth', 'https://ctan.org/pkg/tufte-latex'),
('resume',          'resume',          'cv',           'shimmed',    'Class-package — rSection lives here', NULL),
('exam',            'exam',            'utility',      'partial',    'Class-package — question / problem / solution', 'https://ctan.org/pkg/exam'),
('letter',          'letter',          'layout',       'partial',    'Class-package — letter body environment', NULL)
ON CONFLICT (slug) DO NOTHING;
");

            // =============================================================
            //  Tokens — environments (high signal from prod data)
            // =============================================================
            migrationBuilder.Sql(@"
INSERT INTO latex_tokens (name, kind, package_slug, arity, optional_arity, expects_body, semantic_category, maps_to_block_type, coverage_level, notes) VALUES
('document',        'environment', NULL,          0, 0, true,  'layout',       NULL,        'full',      'Main body — parser enters here'),
('itemize',         'environment', NULL,          0, 0, true,  'list',         'list',      'full',      'Unordered list → list block (ordered:false)'),
('enumerate',       'environment', NULL,          0, 0, true,  'list',         'list',      'full',      'Ordered list → list block (ordered:true)'),
('description',     'environment', NULL,          0, 0, true,  'list',         'list',      'partial',   'Label-term list flattened to plain items'),
('quote',           'environment', NULL,          0, 0, true,  'quote',        'blockquote','full',      'Short quotation'),
('quotation',       'environment', NULL,          0, 0, true,  'quote',        'blockquote','full',      'Longer quotation'),
('verse',           'environment', NULL,          0, 0, true,  'quote',        'blockquote','partial',   'Verse spacing flattened'),
('abstract',        'environment', NULL,          0, 0, true,  'heading',      'abstract',  'full',      'Standard abstract environment'),
('figure',          'environment', NULL,          0, 1, true,  'float',        'figure',    'full',      'Float → figure block'),
('table',           'environment', NULL,          0, 1, true,  'float',        'table',     'full',      'Table float → table block'),
('tabular',         'environment', NULL,          1, 0, true,  'table',        'table',     'full',      'Grid primitive'),
('thebibliography', 'environment', NULL,          1, 0, true,  'bibliography', 'bibliography','full',    'Legacy bib list'),
('verbatim',        'environment', 'verbatim',    0, 0, true,  'code',         'code',      'full',      'Preformatted code → code block'),
('lstlisting',      'environment', 'listings',    0, 1, true,  'code',         'code',      'full',      'Code with language'),
('minted',          'environment', 'minted',      1, 1, true,  'code',         'code',      'partial',   'Pygments highlighting not available in-browser'),
('equation',        'environment', 'amsmath',     0, 0, true,  'math',         'equation',  'full',      'Numbered display math'),
('align',           'environment', 'amsmath',     0, 0, true,  'math',         'equation',  'full',      'Multi-line aligned math'),
('align*',          'environment', 'amsmath',     0, 0, true,  'math',         'equation',  'full',      'Unnumbered aligned math'),
('gather',          'environment', 'amsmath',     0, 0, true,  'math',         'equation',  'full',      'Centered multi-line math'),
('gather*',         'environment', 'amsmath',     0, 0, true,  'math',         'equation',  'full',      'Unnumbered gather'),
('multline',        'environment', 'amsmath',     0, 0, true,  'math',         'equation',  'full',      'Long equation broken across lines'),
('cases',           'environment', 'amsmath',     0, 0, true,  'math',         'equation',  'full',      'Piecewise function'),
('matrix',          'environment', 'amsmath',     0, 0, true,  'math',         'equation',  'full',      'Matrix primitive'),
('pmatrix',         'environment', 'amsmath',     0, 0, true,  'math',         'equation',  'full',      'Parenthesised matrix'),
('bmatrix',         'environment', 'amsmath',     0, 0, true,  'math',         'equation',  'full',      'Bracketed matrix'),
('theorem',         'environment', 'amsthm',      0, 1, true,  'theorem',      'theorem',   'full',      'Named theorem with optional title'),
('lemma',           'environment', 'amsthm',      0, 1, true,  'theorem',      'theorem',   'full',      'Lemma → theorem block with subtype'),
('proposition',     'environment', 'amsthm',      0, 1, true,  'theorem',      'theorem',   'full',      ''),
('corollary',       'environment', 'amsthm',      0, 1, true,  'theorem',      'theorem',   'full',      ''),
('definition',      'environment', 'amsthm',      0, 1, true,  'theorem',      'theorem',   'full',      ''),
('proof',           'environment', 'amsthm',      0, 1, true,  'theorem',      'theorem',   'full',      ''),
('frame',           'environment', 'beamer',      0, 2, true,  'slide',        'slide',     'shimmed',   'Slide in a beamer presentation'),
('block',           'environment', 'beamer',      1, 0, true,  'layout',       NULL,        'partial',   'Beamer coloured block'),
('minipage',        'environment', NULL,          1, 1, true,  'layout',       NULL,        'partial',   'Inner box — passthrough'),
('multicols',       'environment', 'multicol',    1, 0, true,  'layout',       NULL,        'partial',   'Multi-column text'),
('wrapfigure',      'environment', 'wrapfig',     2, 1, true,  'float',        'figure',    'partial',   'Side-floated figure'),
('marginfigure',    'environment', 'tufte-book',  0, 1, true,  'float',        'figure',    'partial',   'Margin float (tufte-book)'),
('fullwidth',       'environment', 'tufte-book',  0, 0, true,  'layout',       NULL,        'partial',   'Full-width insert'),
('twothirdswidth',  'environment', 'tufte-book',  0, 0, true,  'layout',       NULL,        'partial',   'Two-thirds-width insert'),
('appendices',      'environment', 'appendix',    0, 0, true,  'heading',      NULL,        'partial',   'Starts appendix numbering'),
('letter',          'environment', 'letter',      1, 0, true,  'layout',       NULL,        'partial',   'Letter body (letter class)'),
('calendar',        'environment', 'calendar',    0, 0, true,  'utility',      NULL,        'none',      'Calendar typesetting — passthrough only'),
('rSection',        'environment', 'resume',      1, 0, true,  'heading',      'heading',   'shimmed',   'Resume class section → heading with title'),
('question',        'environment', 'exam',        0, 1, true,  'heading',      NULL,        'partial',   'Numbered question block'),
('problem',         'environment', 'exam',        0, 1, true,  'heading',      NULL,        'partial',   'Numbered problem block'),
('solution',        'environment', 'exam',        0, 0, true,  'heading',      NULL,        'partial',   'Solution to a question'),
('tabularx',        'environment', 'tabularx',    2, 0, true,  'table',        'table',     'partial',   'Variable-width columns — flattened to tabular')
ON CONFLICT (name, kind, package_slug) DO NOTHING;
");

            // =============================================================
            //  Tokens — sectioning / inline commands (kernel)
            // =============================================================
            migrationBuilder.Sql(@"
INSERT INTO latex_tokens (name, kind, package_slug, arity, optional_arity, expects_body, semantic_category, maps_to_block_type, coverage_level, notes) VALUES
('part',          'command', NULL,      1, 1, false, 'heading',   'heading', 'full',    'Level 0 heading'),
('chapter',       'command', NULL,      1, 1, false, 'heading',   'heading', 'full',    'Chapter heading'),
('section',       'command', NULL,      1, 1, false, 'heading',   'heading', 'full',    'Top-level section'),
('subsection',    'command', NULL,      1, 1, false, 'heading',   'heading', 'full',    ''),
('subsubsection', 'command', NULL,      1, 1, false, 'heading',   'heading', 'full',    ''),
('paragraph',     'command', NULL,      1, 0, false, 'heading',   'heading', 'partial', 'Treated as H5 — TOC heuristic caveat'),
('subparagraph',  'command', NULL,      1, 0, false, 'heading',   'heading', 'partial', 'Treated as H6'),
('title',         'command', NULL,      1, 0, false, 'heading',   NULL,      'full',    'Document title'),
('author',        'command', NULL,      1, 0, false, 'metadata',  NULL,      'full',    'Stored in ImportMetadata'),
('date',          'command', NULL,      1, 0, false, 'metadata',  NULL,      'full',    'Stored in ImportMetadata'),
('maketitle',     'command', NULL,      0, 0, false, 'metadata',  NULL,      'partial', 'No-op'),
('textbf',        'command', NULL,      1, 0, false, 'inline',    NULL,      'full',    'Bold'),
('textit',        'command', NULL,      1, 0, false, 'inline',    NULL,      'full',    'Italic'),
('textsl',        'command', NULL,      1, 0, false, 'inline',    NULL,      'full',    'Slanted → italic'),
('textsc',        'command', NULL,      1, 0, false, 'inline',    NULL,      'partial', 'Small caps — CSS fallback'),
('texttt',        'command', NULL,      1, 0, false, 'inline',    NULL,      'full',    'Monospace'),
('textrm',        'command', NULL,      1, 0, false, 'inline',    NULL,      'full',    'Serif'),
('textsf',        'command', NULL,      1, 0, false, 'inline',    NULL,      'full',    'Sans'),
('underline',     'command', NULL,      1, 0, false, 'inline',    NULL,      'full',    'Underline'),
('emph',          'command', NULL,      1, 0, false, 'inline',    NULL,      'full',    'Emphasis'),
('item',          'command', NULL,      0, 1, false, 'list',      NULL,      'full',    'List item marker'),
('footnote',      'command', NULL,      1, 0, false, 'annotation',NULL,      'partial', 'Rendered as superscripted marker'),
('newpage',       'command', NULL,      0, 0, false, 'layout',    'pageBreak','full',   'Page break'),
('clearpage',     'command', NULL,      0, 0, false, 'layout',    'pageBreak','full',   'Stronger page break'),
('pagebreak',     'command', NULL,      0, 1, false, 'layout',    'pageBreak','full',   ''),
('linebreak',     'command', NULL,      0, 1, false, 'layout',    NULL,      'partial', 'Soft line break'),
('tableofcontents','command',NULL,      0, 0, false, 'layout',    'tableOfContents','full','→ TOC block'),
('label',         'command', NULL,      1, 0, false, 'reference', NULL,      'full',    'Anchor ids captured'),
('ref',           'command', NULL,      1, 0, false, 'reference', NULL,      'full',    'Reference lookup'),
('eqref',         'command', NULL,      1, 0, false, 'reference', NULL,      'full',    'Equation reference'),
('autoref',       'command', 'hyperref',1, 0, false, 'reference', NULL,      'full',    'Typed cross-reference'),
('cref',          'command', 'cleveref',1, 0, false, 'reference', NULL,      'full',    'Clever reference'),
('pageref',       'command', NULL,      1, 0, false, 'reference', NULL,      'partial', 'Page number reference'),
('cite',          'command', NULL,      1, 1, false, 'citation',  NULL,      'full',    'Generic citation — pill'),
('citep',         'command', 'natbib',  1, 1, false, 'citation',  NULL,      'full',    'Parenthetical cite'),
('citet',         'command', 'natbib',  1, 1, false, 'citation',  NULL,      'full',    'Textual cite'),
('parencite',     'command', 'biblatex',1, 1, false, 'citation',  NULL,      'full',    ''),
('textcite',      'command', 'biblatex',1, 1, false, 'citation',  NULL,      'full',    ''),
('autocite',      'command', 'biblatex',1, 1, false, 'citation',  NULL,      'full',    ''),
('bibliography',  'command', NULL,      1, 0, false, 'citation',  'bibliography','full','Legacy bib loader'),
('printbibliography','command','biblatex',0,1,false, 'citation',  'bibliography','full','Modern bib loader'),
('includegraphics','command','graphicx',1, 1, false, 'float',     'figure',  'full',    'Inserts a figure'),
('caption',       'command', NULL,      1, 1, false, 'float',     NULL,      'full',    'Caption text'),
('url',           'command', 'hyperref',1, 0, false, 'inline',    NULL,      'full',    ''),
('href',          'command', 'hyperref',2, 0, false, 'inline',    NULL,      'full',    ''),
('hspace',        'command', NULL,      1, 1, false, 'layout',    NULL,      'partial', 'Horizontal space'),
('vspace',        'command', NULL,      1, 1, false, 'layout',    NULL,      'partial', 'Vertical space'),
('hfill',         'command', NULL,      0, 0, false, 'layout',    NULL,      'partial', 'Horizontal fill'),
('smallskip',     'command', NULL,      0, 0, false, 'layout',    NULL,      'full',    ''),
('medskip',       'command', NULL,      0, 0, false, 'layout',    NULL,      'full',    ''),
('bigskip',       'command', NULL,      0, 0, false, 'layout',    NULL,      'full',    ''),
('verb',          'command', NULL,      1, 0, false, 'code',      NULL,      'full',    'Inline verbatim'),
('name',          'command', 'moderncv',2, 0, false, 'metadata',  NULL,      'shimmed', 'Split into first / last'),
('phone',         'command', 'moderncv',1, 1, false, 'metadata',  NULL,      'shimmed', ''),
('email',         'command', 'moderncv',1, 0, false, 'metadata',  NULL,      'shimmed', ''),
('address',       'command', 'moderncv',1, 0, false, 'metadata',  NULL,      'shimmed', ''),
('photo',         'command', 'moderncv',1, 2, false, 'metadata',  NULL,      'shimmed', 'Preserved for photo block'),
('cventry',       'command', 'moderncv',6, 0, false, 'cv',        'cvEntry', 'shimmed', 'Maps to cvEntry block'),
('cvitem',        'command', 'moderncv',2, 0, false, 'cv',        'cvEntry', 'shimmed', ''),
('titlepage',     'command', 'beamer',  0, 0, false, 'slide',     'slide',   'shimmed', 'Title slide'),
('frametitle',    'command', 'beamer',  1, 0, false, 'slide',     NULL,      'shimmed', 'Sets slide title'),
('framesubtitle', 'command', 'beamer',  1, 0, false, 'slide',     NULL,      'shimmed', '')
ON CONFLICT (name, kind, package_slug) DO NOTHING;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Seeds are authoritative data — a Down() delete would wipe
            // catalog state the application depends on. Intentionally no-op.
        }
    }
}
