using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// The connector's daily allowance (the owner, 5 Nov 2026: "Free 50/day, Pro unlimited"): Free gets
    /// 50 actions a day through the Lilia connector, every other plan none to count (-1). Read by
    /// ConnectorAllowance, and shown in the plans dialog from the same cap. Data only.
    /// </summary>
    public partial class PlansConnectorDailyAllowance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE plans SET caps = jsonb_set(caps, '{mcpActionsPerDay}', '50'), updated_at = NOW() WHERE slug = 'free';
UPDATE plans SET caps = jsonb_set(caps, '{mcpActionsPerDay}', '-1'), updated_at = NOW() WHERE slug <> 'free';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE plans SET caps = caps - 'mcpActionsPerDay', updated_at = NOW();");
        }
    }
}
