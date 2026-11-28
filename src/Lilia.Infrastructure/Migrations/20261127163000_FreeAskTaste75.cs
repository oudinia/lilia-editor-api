using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Free's Ask Lilia allowance, as the owner decided on 27 Nov 2026: 75 credits a month (was 30). Unused credits
    /// still roll over (<c>AskRollover</c>). Data only; the model is unchanged. Down restores 30.
    /// </summary>
    public partial class FreeAskTaste75 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = jsonb_set(caps, '{aiCreditsPerMonth}', '75'), updated_at = NOW() WHERE slug = 'free';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = jsonb_set(caps, '{aiCreditsPerMonth}', '30'), updated_at = NOW() WHERE slug = 'free';
");
        }
    }
}
