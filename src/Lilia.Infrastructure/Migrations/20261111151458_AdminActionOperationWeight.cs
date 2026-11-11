using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>An admin changing an operation kind's weight (the console's Operations page, 11 Nov 2026) is logged like the other admin actions.</summary>
    public partial class AdminActionOperationWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_admin_action_action",
                table: "admin_actions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_admin_action_action",
                table: "admin_actions",
                sql: "action IN ('grant_credits','set_plan','suspend','unsuspend','resend_verification','feedback_status','two_step_off','operation_weight')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_admin_action_action",
                table: "admin_actions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_admin_action_action",
                table: "admin_actions",
                sql: "action IN ('grant_credits','set_plan','suspend','unsuspend','resend_verification','feedback_status','two_step_off')");
        }
    }
}
