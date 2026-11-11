using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// PDF import is paid (the owner, 11 Nov 2026: "pdf import should not be free", well managed). Mathpix charges per
    /// page, so: an operation kind for it, counted by pages (5 points, plus 1 a page); Pro gets a monthly page allowance
    /// (pdfPagesPerMonth, 100 until the owner sets it); Free gets none (0, said explicitly). Data only; the model is
    /// unchanged.
    /// </summary>
    public partial class PdfImportPaid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO operation_kinds (code, category, label, points, points_per_unit, unit_size, unit_name)
VALUES ('import.pdf', 'import', 'Import a PDF (read by Mathpix, paid per page)', 5, 1, 1, 'pages')
ON CONFLICT (code) DO NOTHING;
UPDATE plans SET caps = jsonb_set(caps, '{pdfPagesPerMonth}', '100'), updated_at = NOW() WHERE slug = 'pro';
UPDATE plans SET caps = jsonb_set(caps, '{pdfPagesPerMonth}', '0'), updated_at = NOW() WHERE slug = 'free';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = caps - 'pdfPagesPerMonth', updated_at = NOW() WHERE slug IN ('pro', 'free');
DELETE FROM operation_kinds k WHERE k.code = 'import.pdf' AND NOT EXISTS (SELECT 1 FROM operations o WHERE o.kind = k.code);
");
        }
    }
}
