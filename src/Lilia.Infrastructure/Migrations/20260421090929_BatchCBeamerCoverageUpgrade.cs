using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BatchCBeamerCoverageUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Batch C of the Q2 coverage plan — beamer frame + frametitle +
            // framesubtitle + titlepage all get parse-time rewrites (see
            // LatexParser.NormaliseCoverageEnvironments). Coverage moves
            // to 'full' with a note flagging lossy export back to beamer.
            // Closes 41 hits / 7 days — the biggest single coverage win.

            migrationBuilder.Sql(@"
UPDATE latex_tokens
SET coverage_level = 'full',
    notes = 'Rewritten to \\section*{Title} at parse time by LatexParser.NormaliseCoverageEnvironments. Frame semantics are lost on import — export back to beamer reconstructs from class metadata.',
    updated_at = NOW()
WHERE name = 'frame' AND kind = 'environment' AND package_slug = 'beamer';

UPDATE latex_tokens
SET coverage_level = 'full',
    notes = 'Parse-time rewrite to \\section*{Title}. Works inside any \\begin{frame} body.',
    updated_at = NOW()
WHERE name = 'frametitle' AND kind = 'command' AND package_slug = 'beamer';

UPDATE latex_tokens
SET coverage_level = 'full',
    notes = 'Parse-time rewrite to a bold paragraph. No kernel equivalent for subtitle.',
    updated_at = NOW()
WHERE name = 'framesubtitle' AND kind = 'command' AND package_slug = 'beamer';

UPDATE latex_tokens
SET coverage_level = 'full',
    notes = 'Parse-time rewrite to \\maketitle — picks up the preamble \\title / \\author / \\date.',
    updated_at = NOW()
WHERE name = 'titlepage' AND kind = 'command' AND package_slug = 'beamer';

UPDATE latex_packages
SET coverage_level = 'partial',
    coverage_notes = 'Frames rewritten to sections at import — navigable but slide semantics lost. Beamer-specific features (themes, transitions, overlays) preserved as class metadata for export, not rendered in preview.',
    updated_at = NOW()
WHERE slug = 'beamer';

UPDATE latex_document_classes
SET coverage_level = 'partial',
    notes = 'Frames collapsed to sections at parse time — presentation structure preserved as navigable headings. Round-trip export back to beamer requires class metadata (document.latexDocumentClass).',
    updated_at = NOW()
WHERE slug = 'beamer';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE latex_tokens SET coverage_level = 'shimmed' WHERE name = 'frame' AND kind = 'environment' AND package_slug = 'beamer';
UPDATE latex_tokens SET coverage_level = 'shimmed' WHERE name IN ('frametitle', 'framesubtitle', 'titlepage') AND kind IN ('command', 'environment') AND package_slug = 'beamer';
UPDATE latex_document_classes SET coverage_level = 'shimmed' WHERE slug = 'beamer';
");
        }
    }
}
