using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BatchACoverageUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Batch A of the Q2 coverage plan — tabularx + rSection are
            // now rewritten at parse time by
            // LatexParser.NormaliseCoverageEnvironments, so their catalog
            // coverage moves to 'full'. Calendar and appendices stay as
            // passthrough but with explicit diagnostic notes.

            migrationBuilder.Sql(@"
UPDATE latex_tokens
SET coverage_level = 'full',
    notes = 'Rewritten to \\begin{tabular} at parse time by LatexParser.NormaliseCoverageEnvironments. X columns degrade to l.',
    updated_at = NOW()
WHERE name = 'tabularx' AND kind = 'environment' AND package_slug = 'tabularx';

UPDATE latex_packages
SET coverage_level = 'full',
    coverage_notes = 'tabularx environments flattened to tabular at import — variable-width X columns render as left-aligned.',
    updated_at = NOW()
WHERE slug = 'tabularx';

UPDATE latex_tokens
SET coverage_level = 'full',
    notes = 'Resume-class rSection rewritten to \\section*{Title} at parse time by LatexParser.NormaliseCoverageEnvironments.',
    updated_at = NOW()
WHERE name = 'rSection' AND kind = 'environment' AND package_slug = 'resume';

UPDATE latex_tokens
SET coverage_level = 'partial',
    notes = 'Preserved as passthrough with LATEX.PASSTHROUGH.APPENDICES diagnostic. Inner content parsed as normal blocks.',
    updated_at = NOW()
WHERE name = 'appendices' AND kind = 'environment' AND package_slug = 'appendix';

UPDATE latex_tokens
SET coverage_level = 'none',
    notes = 'Preserved as raw LaTeX passthrough with LATEX.PASSTHROUGH.CALENDAR diagnostic. Not rendered in preview.',
    updated_at = NOW()
WHERE name = 'calendar' AND kind = 'environment' AND package_slug = 'calendar';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE latex_tokens SET coverage_level = 'partial' WHERE name = 'tabularx' AND kind = 'environment' AND package_slug = 'tabularx';");
            migrationBuilder.Sql("UPDATE latex_packages SET coverage_level = 'partial' WHERE slug = 'tabularx';");
            migrationBuilder.Sql("UPDATE latex_tokens SET coverage_level = 'shimmed' WHERE name = 'rSection' AND kind = 'environment' AND package_slug = 'resume';");
        }
    }
}
