using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    admin_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    target_user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    details = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_actions", x => x.id);
                    table.CheckConstraint("ck_admin_action_action", "action IN ('grant_credits','set_plan','suspend','unsuspend','resend_verification','feedback_status')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_admin_actions_created",
                table: "admin_actions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_admin_actions_target",
                table: "admin_actions",
                columns: new[] { "target_user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_actions");
        }
    }
}
