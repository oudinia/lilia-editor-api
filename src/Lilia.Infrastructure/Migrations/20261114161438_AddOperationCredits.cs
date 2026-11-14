using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operation_credits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tool = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    stripe_session_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    refund_of = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_credits", x => x.id);
                    table.CheckConstraint("ck_operation_credit_reason", "reason IN ('purchase','spend','refund','adjustment')");
                    table.ForeignKey(
                        name: "FK_operation_credits_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_operation_credits_user",
                table: "operation_credits",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_operation_credits_refund_of",
                table: "operation_credits",
                column: "refund_of",
                unique: true,
                filter: "refund_of IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_operation_credits_stripe_session",
                table: "operation_credits",
                column: "stripe_session_id",
                unique: true,
                filter: "stripe_session_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operation_credits");
        }
    }
}
