using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// The connector-first launch, as the owner decided on 10 Nov 2026 (report 2026-11-10-launch-direction): Free gets
    /// 25 connector actions a day (was 50), enough to make and export a short paper from an AI app; Pro Plus is hidden
    /// (inactive: not listed, not sold) while Ask Lilia is "coming soon", since more AI is all it adds. Anyone already on
    /// Pro Plus keeps it. Data only; the model is unchanged.
    /// </summary>
    public partial class ConnectorFirstPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = jsonb_set(caps, '{mcpActionsPerDay}', '25'), updated_at = NOW() WHERE slug = 'free';
UPDATE plans SET is_active = FALSE, updated_at = NOW() WHERE slug = 'pro_plus';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = jsonb_set(caps, '{mcpActionsPerDay}', '50'), updated_at = NOW() WHERE slug = 'free';
UPDATE plans SET is_active = TRUE, updated_at = NOW() WHERE slug = 'pro_plus';
");
        }
    }
}
