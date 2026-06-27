using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChangelogEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "changelog_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    area = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "fix"),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "shipped"),
                    title = table.Column<string>(type: "jsonb", nullable: false),
                    detail = table.Column<string>(type: "jsonb", nullable: false),
                    verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    shot_url = table.Column<string>(type: "text", nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_changelog_entries", x => x.id);
                    table.CheckConstraint("ck_changelog_kind", "kind IN ('fix','feature')");
                    table.CheckConstraint("ck_changelog_status", "status IN ('shipped','known')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_changelog_date_sort",
                table: "changelog_entries",
                columns: new[] { "entry_date", "sort" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "changelog_entries");
        }
    }
}
