using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Free's Ask Lilia allowance, as the owner decided on 10 Nov 2026 (option A of the allowance decision): 20 credits
    /// a month (was 100), a taste of Ask Lilia in the author's own paper; past it, Lilia offers the connector (the
    /// author's own AI) and the downloadable skill. Sized for a launch month of about 100 users on a small prepaid
    /// budget: the free pool stays $15 a month, the site cap rises to $75. Data only; the model is unchanged.
    /// </summary>
    public partial class FreeAskAllowance20 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = jsonb_set(caps, '{aiCreditsPerMonth}', '20'), updated_at = NOW() WHERE slug = 'free';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = jsonb_set(caps, '{aiCreditsPerMonth}', '100'), updated_at = NOW() WHERE slug = 'free';
");
        }
    }
}
