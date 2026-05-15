using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TeamCodeAndMembersAndDefaultTeam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reconcile pre-EF drift: prod was bootstrapped from
            // database/001_create_lilia_core.sql which defined a *view*
            // named "team_members" (a flat projection over groups +
            // group_members + roles). Our new entity needs a real TABLE
            // at that name, and Postgres treats views and tables as the
            // same kind of relation — so without this drop, EF's
            // CreateTable fails with "relation team_members already
            // exists" (SqlState 42P07) and the container crash-loops.
            // Idempotent: no-op if the view never existed (fresh DBs).
            migrationBuilder.Sql("DROP VIEW IF EXISTS team_members CASCADE;");

            migrationBuilder.AddColumn<Guid>(
                name: "default_team_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plan",
                table: "teams",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "free");

            migrationBuilder.AddColumn<string>(
                name: "team_code",
                table: "teams",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "team_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "member"),
                    invited_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_members", x => x.id);
                    table.CheckConstraint("ck_team_member_role", "role IN ('owner','admin','member','viewer')");
                    table.ForeignKey(
                        name: "FK_team_members_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_team_members_users_invited_by",
                        column: x => x.invited_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_team_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_default_team_id",
                table: "users",
                column: "default_team_id");

            migrationBuilder.CreateIndex(
                name: "IX_teams_team_code",
                table: "teams",
                column: "team_code",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_team_plan",
                table: "teams",
                sql: "plan IN ('free','pro','team')");

            migrationBuilder.CreateIndex(
                name: "IX_team_members_invited_by",
                table: "team_members",
                column: "invited_by");

            migrationBuilder.CreateIndex(
                name: "IX_team_members_team_id_user_id",
                table: "team_members",
                columns: new[] { "team_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_members_user_id",
                table: "team_members",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_users_teams_default_team_id",
                table: "users",
                column: "default_team_id",
                principalTable: "teams",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_teams_default_team_id",
                table: "users");

            migrationBuilder.DropTable(
                name: "team_members");

            migrationBuilder.DropIndex(
                name: "IX_users_default_team_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_teams_team_code",
                table: "teams");

            migrationBuilder.DropCheckConstraint(
                name: "ck_team_plan",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "default_team_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "plan",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "team_code",
                table: "teams");
        }
    }
}
