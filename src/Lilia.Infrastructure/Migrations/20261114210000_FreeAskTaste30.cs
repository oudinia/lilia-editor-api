using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Free's Ask Lilia taste for when it comes back, as the owner decided on 14 Nov 2026 ("Small free taste"): 30 credits
    /// a month (was 20), about two questions; Pro keeps 500. Unused credits roll over (AskRollover). Data only; the model
    /// is unchanged.
    /// </summary>
    public partial class FreeAskTaste30 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = jsonb_set(caps, '{aiCreditsPerMonth}', '30'), updated_at = NOW() WHERE slug = 'free';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = jsonb_set(caps, '{aiCreditsPerMonth}', '20'), updated_at = NOW() WHERE slug = 'free';
");
        }
    }
}
