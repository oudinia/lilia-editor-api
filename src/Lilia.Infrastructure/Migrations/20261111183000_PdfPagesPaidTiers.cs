using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// PDF pages for every paid plan, not only Pro (11 Nov 2026): PdfImportPaid gave Pro 100 pages a month and Free none,
    /// which left the plans above Pro (Pro Plus, Team, Enterprise, Compliance Pro, Beta) refused, since a plan without
    /// pdfPagesPerMonth gets no PDF import. They get Pro's 100 until the owner sets their numbers; the single-purpose
    /// plans (Student, Conversion, ePub) get none, said explicitly. Data only; the model is unchanged.
    /// </summary>
    public partial class PdfPagesPaidTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = jsonb_set(caps, '{pdfPagesPerMonth}', '100'), updated_at = NOW()
 WHERE slug IN ('pro_plus', 'team', 'enterprise', 'compliance_pro', 'beta');
UPDATE plans SET caps = jsonb_set(caps, '{pdfPagesPerMonth}', '0'), updated_at = NOW()
 WHERE slug IN ('student', 'conversion', 'epub');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = caps - 'pdfPagesPerMonth', updated_at = NOW()
 WHERE slug IN ('pro_plus', 'team', 'enterprise', 'compliance_pro', 'beta', 'student', 'conversion', 'epub');
");
        }
    }
}
