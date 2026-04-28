using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTypstCoverageTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "typst_translation_gaps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    gap_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sample_pattern = table.Column<string>(type: "text", nullable: false),
                    typst_error_shape = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    mitigation_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "none"),
                    blocking_severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "info"),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_typst_translation_gaps", x => x.id);
                    table.CheckConstraint("ck_typst_gap_mitigation", "mitigation_status IN ('none','workaround','scheduled','shipped')");
                    table.CheckConstraint("ck_typst_gap_severity", "blocking_severity IN ('info','warn','error')");
                });

            migrationBuilder.CreateTable(
                name: "typst_translation_handlers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    handler_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_pattern = table.Column<string>(type: "text", nullable: false),
                    typst_emit = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "active"),
                    shipped_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    shipped_in = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_typst_translation_handlers", x => x.id);
                    table.CheckConstraint("ck_typst_handler_status", "status IN ('active','deprecated','planned')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_typst_gap_mitigation_severity",
                table: "typst_translation_gaps",
                columns: new[] { "mitigation_status", "blocking_severity" });

            migrationBuilder.CreateIndex(
                name: "ux_typst_gap_key",
                table: "typst_translation_gaps",
                column: "gap_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_typst_handler_category_status",
                table: "typst_translation_handlers",
                columns: new[] { "category", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_typst_handler_key",
                table: "typst_translation_handlers",
                column: "handler_key",
                unique: true);

            // Seed catalog from current shipped state. Each row encodes
            // a translation rule live in TypstExportService as of the
            // 2026-04-28 launch-eve push. ON CONFLICT updates so the
            // migration is idempotent if re-applied via direct SQL.
            migrationBuilder.Sql(@"
INSERT INTO typst_translation_handlers (handler_key, category, source_pattern, typst_emit, status, shipped_in, notes) VALUES
  ('math.mathbb',          'math',     '\mathbb{X}',     'bb(X)',          'active', '2026-04-28', 'blackboard bold'),
  ('math.mathcal',         'math',     '\mathcal{X}',    'cal(X)',         'active', '2026-04-28', 'calligraphic'),
  ('math.mathbf',          'math',     '\mathbf{X}',     'bold(X)',        'active', '2026-04-28', NULL),
  ('math.mathit',          'math',     '\mathit{X}',     'italic(X)',      'active', '2026-04-28', NULL),
  ('math.mathrm',          'math',     '\mathrm{X}',     'upright(X)',     'active', '2026-04-28', NULL),
  ('math.mathsf',          'math',     '\mathsf{X}',     'sans(X)',        'active', '2026-04-28', NULL),
  ('math.mathtt',          'math',     '\mathtt{X}',     'mono(X)',        'active', '2026-04-28', NULL),
  ('math.mathfrak',        'math',     '\mathfrak{X}',   'frak(X)',        'active', '2026-04-28', NULL),
  ('math.text',            'math',     '\text{X}',       '""X""',            'active', '2026-04-28', 'upright text in math'),
  ('math.frac',            'math',     '\frac{a}{b}',    'frac(a, b)',     'active', '2026-04-28', 'iterated for nested fracs'),
  ('math.sqrt',            'math',     '\sqrt{x}',       'sqrt(x)',        'active', '2026-04-28', NULL),
  ('math.root',            'math',     '\sqrt[n]{x}',    'root(n, x)',     'active', '2026-04-28', 'nth root'),
  ('math.subscript',       'math',     '_{X}',           '_(X)',           'active', '2026-04-28', 'multi-char subscript'),
  ('math.superscript',     'math',     '^{X}',           '^(X)',           'active', '2026-04-28', 'multi-char superscript'),
  ('math.int',             'math',     '\int',           'integral',       'active', '2026-04-28', NULL),
  ('math.iint',            'math',     '\iint',          'integral.double','active', '2026-04-28', NULL),
  ('math.iiint',           'math',     '\iiint',         'integral.triple','active', '2026-04-28', NULL),
  ('math.oint',            'math',     '\oint',          'integral.cont',  'active', '2026-04-28', NULL),
  ('math.prod',            'math',     '\prod',          'product',        'active', '2026-04-28', NULL),
  ('math.coprod',          'math',     '\coprod',        'product.co',     'active', '2026-04-28', NULL),
  ('math.lim',             'math',     '\lim',           'limits.lim',     'active', '2026-04-28', NULL),
  ('math.limsup',          'math',     '\limsup',        'limits.lim.sup', 'active', '2026-04-28', NULL),
  ('math.liminf',          'math',     '\liminf',        'limits.lim.inf', 'active', '2026-04-28', NULL),
  ('math.bare-ops',        'math',     '\sum/\sin/\cos/\log/\exp/...', '<bare>', 'active', '2026-04-28', '24 bare-strip operators'),
  ('greek.lower',          'math',     '\alpha..\omega', '<bare>',         'active', '2026-04-28', '29 lowercase greek letters'),
  ('greek.upper',          'math',     '\Gamma..\Omega', '<bare>',         'active', '2026-04-28', '11 uppercase greek letters'),
  ('spacing.quad',         'spacing',  '\quad',          'quad',           'active', '2026-04-28', NULL),
  ('spacing.qquad',        'spacing',  '\qquad',         'wide',           'active', '2026-04-28', NULL),
  ('spacing.thin',         'spacing',  '\,',             'thin',           'active', '2026-04-28', NULL),
  ('spacing.med',          'spacing',  '\;/\:',          'med',            'active', '2026-04-28', NULL),
  ('spacing.neg',          'spacing',  '\!',             '<stripped>',     'active', '2026-04-28', 'negative thin space'),
  ('spacing.linebr',       'spacing',  '\\\\',           '\ ',             'active', '2026-04-28', 'math line break'),
  ('matrix.pmatrix',       'matrix',   '\begin{pmatrix}...\end{pmatrix}', 'mat(delim: ""("", ...)', 'active', '2026-04-28', NULL),
  ('matrix.bmatrix',       'matrix',   '\begin{bmatrix}...\end{bmatrix}', 'mat(delim: ""["", ...)', 'active', '2026-04-28', NULL),
  ('matrix.Bmatrix',       'matrix',   '\begin{Bmatrix}...\end{Bmatrix}', 'mat(delim: ""{"", ...)', 'active', '2026-04-28', NULL),
  ('matrix.vmatrix',       'matrix',   '\begin{vmatrix}...\end{vmatrix}', 'mat(delim: ""|"", ...)', 'active', '2026-04-28', NULL),
  ('matrix.Vmatrix',       'matrix',   '\begin{Vmatrix}...\end{Vmatrix}', 'mat(delim: ""||"", ...)','active', '2026-04-28', NULL),
  ('matrix.bare',          'matrix',   '\begin{matrix}...\end{matrix}',   'mat(...)',         'active', '2026-04-28', 'no delimiters'),
  ('citation.cite-native', 'citation', '\cite{X}',       '@X',             'active', '2026-04-28', 'requires references.bib alongside main.typ'),
  ('citation.cite-alias',  'citation', '@cite{X}',       '@X',             'active', '2026-04-28', 'editor alias form'),
  ('reference.ref',        'reference','\ref{X}',        '@X',             'active', '2026-04-28', NULL),
  ('reference.eqref',      'reference','\eqref{X}',      '@X',             'active', '2026-04-28', NULL),
  ('link.url',             'link',     '\url{X}',        '#link(""X"")',     'active', '2026-04-28', NULL),
  ('link.href',            'link',     '\href{X}{Y}',    '#link(""X"")[Y]',  'active', '2026-04-28', NULL),
  ('footnote.basic',       'footnote', '\footnote{X}',   '#footnote[X]',   'active', '2026-04-28', NULL),
  ('label.basic',          'label',    '\label{X}',      '<X>',            'active', '2026-04-28', 'inline label position-tagged'),
  ('layout.noindent',      'layout',   '\noindent',      '<stripped>',     'active', '2026-04-28', NULL),
  ('layout.hfill',         'layout',   '\hfill',         '#h(1fr)',        'active', '2026-04-28', NULL),
  ('layout.par',           'layout',   '\par',           '<blank line>',   'active', '2026-04-28', NULL),
  ('layout.skips',         'layout',   '\medskip/\smallskip/\bigskip', '<stripped>', 'active', '2026-04-28', NULL),
  ('inline.linebreak',     'inline',   '\\\\',           'Typst line break','active','2026-04-28', NULL),
  ('inline.textbf',        'inline',   '\textbf{X}',     '**X**',          'active', '2026-04-28', 'routes through markdown'),
  ('inline.textit',        'inline',   '\textit{X}',     '*X*',            'active', '2026-04-28', NULL),
  ('inline.emph',          'inline',   '\emph{X}',       '*X*',            'active', '2026-04-28', NULL),
  ('inline.texttt',        'inline',   '\texttt{X}',     '`X`',            'active', '2026-04-28', NULL),
  ('inline.underline',     'inline',   '\underline{X}',  '#underline[X]',  'active', '2026-04-28', NULL),
  ('inline.textsc',        'inline',   '\textsc{X}',     '#smallcaps[X]',  'active', '2026-04-28', NULL),
  ('inline.enquote',       'inline',   '\enquote{X}',    '""X""',            'active', '2026-04-28', NULL),
  ('inline.section-cmds',  'inline',   '\section/\subsection/\paragraph', '= / == / ====', 'active', '2026-04-28', 'sectioning fallback for in-paragraph use'),
  ('math.display-dollar',  'math',     '$$X$$',          '$ X $',          'active', '2026-04-28', 'display math via spacing'),
  ('figure.placeholder-url','figure',  'http(s):// / /api/placeholder/...', '#rect(...)', 'active', '2026-04-28', 'unresolvable URL → drawn rect'),
  ('heading.title-dedup',  'heading',  'leading H1 == doc.title', '<dropped>', 'active', '2026-04-28', 'avoids title appearing twice'),
  ('heading.strip-prefix', 'heading',  '""1. X"" / ""1.1 X"" / ""IV. X"" / ""A. X""', '""X""', 'active', '2026-04-28', 'mirror of import-side strip'),
  ('preamble.font-fallback','preamble','#set text(font: ...)', '(""Linux Libertine"", ""New Computer Modern"")', 'active', '2026-04-28', 'NCM bundled with typst CLI')
ON CONFLICT (handler_key) DO UPDATE SET
  category = EXCLUDED.category,
  source_pattern = EXCLUDED.source_pattern,
  typst_emit = EXCLUDED.typst_emit,
  status = EXCLUDED.status,
  shipped_in = EXCLUDED.shipped_in,
  notes = EXCLUDED.notes;

INSERT INTO typst_translation_gaps (gap_key, category, sample_pattern, typst_error_shape, mitigation_status, blocking_severity, notes) VALUES
  ('math.two-letter-identifier', 'math',
    'g^(ab) — adjacent letters in math',
    'unknown variable: %s',
    'none', 'info',
    'Typst math treats `ab` as one identifier; LaTeX as `a*b`. Doc falls back to pdflatex; ~6-8s vs <3s on Typst path. Safe-space-insertion would risk breaking sin/cos/log/etc. function names.'),
  ('matrix.deeply-nested', 'matrix',
    '\begin{pmatrix} \begin{pmatrix} a \end{pmatrix} \end{pmatrix}',
    'expected end of matrix env',
    'none', 'info',
    'Current matrix conversion regex doesn''t recurse into nested matrices. Rare in practice.'),
  ('verbatim.literal-blocks', 'verbatim',
    '\begin{verbatim}...\end{verbatim}',
    NULL,
    'none', 'info',
    'No translation; whole block falls to pdflatex. Code blocks separately handled via Typst raw strings.'),
  ('table.complex-tabular', 'table',
    '\begin{tabular}{l|c|r} with \\hline rules + \\multicolumn',
    NULL,
    'workaround', 'warn',
    'Simple tabular routes through table-block path. Complex multi-column / hline-heavy tables fall back.'),
  ('package.tikz', 'package',
    '\begin{tikzpicture}...\end{tikzpicture}',
    NULL,
    'none', 'warn',
    'TikZ pictures untranslated. pdflatex fallback always works for TikZ.')
ON CONFLICT (gap_key) DO UPDATE SET
  category = EXCLUDED.category,
  sample_pattern = EXCLUDED.sample_pattern,
  typst_error_shape = EXCLUDED.typst_error_shape,
  mitigation_status = EXCLUDED.mitigation_status,
  blocking_severity = EXCLUDED.blocking_severity,
  notes = EXCLUDED.notes;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "typst_translation_gaps");

            migrationBuilder.DropTable(
                name: "typst_translation_handlers");
        }
    }
}
