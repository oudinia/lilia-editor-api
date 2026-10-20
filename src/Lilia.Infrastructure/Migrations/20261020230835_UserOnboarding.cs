using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_onboarding",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    welcome_sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_document_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_document_route = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tour_step_reached = table.Column<int>(type: "integer", nullable: true),
                    tour_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tour_restarts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    first_validate_pass_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_export_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    follow_up_sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tips_opt_out = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_onboarding", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_user_onboarding_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_onboarding_created_at",
                table: "user_onboarding",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_onboarding");
        }
    }
}
