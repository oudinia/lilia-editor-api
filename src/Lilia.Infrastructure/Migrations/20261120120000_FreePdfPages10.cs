using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Free imports PDFs and scans (the owner, 20 Nov 2026): 10 PDF pages a month (was 0), each page also costing 10 of the
    /// connector's credits (<c>Mathpix:CreditsPerPage</c>). Pro keeps 100 and the other plans their own. Data only; the
    /// model is unchanged. Down restores 0.
    /// </summary>
    public partial class FreePdfPages10 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = jsonb_set(caps, '{pdfPagesPerMonth}', '10'), updated_at = NOW() WHERE slug = 'free';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = jsonb_set(caps, '{pdfPagesPerMonth}', '0'), updated_at = NOW() WHERE slug = 'free';
");
        }
    }
}
