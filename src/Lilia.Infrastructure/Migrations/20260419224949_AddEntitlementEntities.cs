using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEntitlementEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_credit_ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ai_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_credit_ledger", x => x.id);
                    table.CheckConstraint("ck_ai_credit_reason", "reason IN ('grant','spend','adjustment','refund')");
                    table.ForeignKey(
                        name: "FK_ai_credit_ledger_ai_requests_ai_request_id",
                        column: x => x.ai_request_id,
                        principalTable: "ai_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ai_credit_ledger_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    slug = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    display_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    monthly_price = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    yearly_price = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    caps = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    features = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plans", x => x.id);
                    table.CheckConstraint("ck_plan_slug", "slug IN ('free','student','pro','team','epub','compliance_pro','enterprise')");
                });

            migrationBuilder.CreateTable(
                name: "user_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "active"),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    current_period_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    current_period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    external_ref = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    cancel_at_period_end = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_plans", x => x.id);
                    table.CheckConstraint("ck_user_plan_status", "status IN ('active','trial','past_due','cancelled')");
                    table.ForeignKey(
                        name: "FK_user_plans_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_plans_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_credit_ledger_ai_request_id",
                table: "ai_credit_ledger",
                column: "ai_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_credit_user",
                table: "ai_credit_ledger",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_credit_user_time",
                table: "ai_credit_ledger",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_plan_slug",
                table: "plans",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_plan_external_ref",
                table: "user_plans",
                column: "external_ref");

            migrationBuilder.CreateIndex(
                name: "ix_user_plan_user",
                table: "user_plans",
                column: "user_id",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_user_plans_plan_id",
                table: "user_plans",
                column: "plan_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_credit_ledger");

            migrationBuilder.DropTable(
                name: "user_plans");

            migrationBuilder.DropTable(
                name: "plans");
        }
    }
}
