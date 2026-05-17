using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RetireTeamMembersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill before drop — preserve every membership from
            // team_members as a group_members row.
            //
            // The two tables existed in parallel: the user-create +
            // invite flows wrote to group_members (the modern,
            // group-scoped membership), the Wolverine auto-mint
            // handler wrote to team_members. The rest of the app
            // (member list, remove-member, set-doc-team after the
            // 2026-05-17 fix) reads exclusively from group_members.
            // So team_members rows were "real" memberships that
            // never showed up anywhere the user could see them.
            //
            // Strategy:
            //   1. For every team referenced by team_members that
            //      doesn't already have a default Group, create one
            //      ("Everyone", is_default = true). This matches what
            //      the user-create path does on team creation.
            //   2. For every team_members row, insert the equivalent
            //      group_members row in that team's default group,
            //      mapped to the seeded role id (owner/editor/viewer).
            //      Role values 'admin' and 'member' fold to 'editor'
            //      to match the canonical RoleNames set; that's the
            //      same normalization RoleNames.Normalize uses.
            //   3. Skip rows where the equivalent group_members row
            //      already exists — keeps the migration idempotent
            //      and protects against the rare case where a user
            //      had both kinds of memberships in the same team.
            //
            // After the backfill the team_members table is dropped.
            migrationBuilder.Sql(@"
                -- 1) Ensure each team referenced by team_members has a default Group.
                INSERT INTO groups (id, team_id, name, is_default, created_at)
                SELECT gen_random_uuid(), tm.team_id, 'Everyone', TRUE, NOW()
                FROM (SELECT DISTINCT team_id FROM team_members) tm
                LEFT JOIN groups g
                  ON g.team_id = tm.team_id AND g.is_default = TRUE
                WHERE g.id IS NULL;

                -- 2) Insert one group_members row per team_members row,
                --    mapped to the team's default group + canonical role id.
                INSERT INTO group_members (id, group_id, user_id, role_id, created_at)
                SELECT
                  gen_random_uuid(),
                  g.id,
                  tm.user_id,
                  CASE LOWER(tm.role)
                    WHEN 'owner'  THEN '00000000-0000-0000-0000-000000000001'::uuid
                    WHEN 'viewer' THEN '00000000-0000-0000-0000-000000000003'::uuid
                    -- editor/admin/member all map to editor (canonical).
                    ELSE '00000000-0000-0000-0000-000000000002'::uuid
                  END,
                  COALESCE(tm.joined_at, NOW())
                FROM team_members tm
                JOIN groups g
                  ON g.team_id = tm.team_id AND g.is_default = TRUE
                LEFT JOIN group_members existing
                  ON existing.group_id = g.id AND existing.user_id = tm.user_id
                WHERE existing.id IS NULL;
            ");

            migrationBuilder.DropTable(
                name: "team_members");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "team_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    invited_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "member")
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
        }
    }
}
