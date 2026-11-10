using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Pro's connector actions get a safety cap of 500 a day (the owner, 10 Nov 2026, Pro "option A": unlimited documents
    /// and exports, a high daily connector limit as a safety cap only). A real author never reaches it; a runaway app does.
    /// Data only; the model is unchanged.
    /// </summary>
    public partial class ProConnectorSafetyCap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = jsonb_set(caps, '{mcpActionsPerDay}', '500'), updated_at = NOW() WHERE slug = 'pro';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = jsonb_set(caps, '{mcpActionsPerDay}', '-1'), updated_at = NOW() WHERE slug = 'pro';
");
        }
    }
}
