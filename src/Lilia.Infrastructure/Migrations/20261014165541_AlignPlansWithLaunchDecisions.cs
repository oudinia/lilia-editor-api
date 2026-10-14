using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// The plans as decided on 14 Oct 2026 (lilia-docs design-handoffs/2026-10-14-launch-redesign, "Decisions"):
    /// the free plan's limit is documents, never output, so no export page cut and every export format;
    /// validation and sharing are free; Ask Lilia gets a small monthly allowance on Free and a larger one
    /// on Pro (a credit is ~$0.006, so 100 ≈ $0.60 and 800 ≈ $4.80 a month at most). Beta stays Pro.
    /// No third plan at launch: the one-off Conversion plan is switched off.
    /// </summary>
    public partial class AlignPlansWithLaunchDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET
  caps = '{""maxDocs"":3,""maxImportsPerMonth"":3,""aiCreditsPerMonth"":100,""maxTeamSeats"":0,""exportPageLimit"":-1}'::jsonb,
  features = '[""editor"",""export_pdf"",""export_latex"",""export_docx"",""export_html"",""export_markdown"",""validation"",""share_links"",""templates""]'::jsonb,
  updated_at = NOW()
WHERE slug = 'free';

UPDATE plans SET
  caps = jsonb_set(caps, '{aiCreditsPerMonth}', '800'),
  updated_at = NOW()
WHERE slug IN ('pro', 'beta');

UPDATE plans SET is_active = false, updated_at = NOW()
WHERE slug = 'conversion';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET
  caps = '{""maxDocs"":3,""maxImportsPerMonth"":3,""aiCreditsPerMonth"":0,""maxTeamSeats"":0,""exportPageLimit"":10}'::jsonb,
  features = '[""editor"",""export_pdf"",""export_latex""]'::jsonb,
  updated_at = NOW()
WHERE slug = 'free';

UPDATE plans SET
  caps = jsonb_set(caps, '{aiCreditsPerMonth}', '0'),
  updated_at = NOW()
WHERE slug IN ('pro', 'beta');

UPDATE plans SET is_active = true, updated_at = NOW()
WHERE slug = 'conversion';
");
        }
    }
}
