using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLatexInsertionEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "latex_insertion_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    token_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    token_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token_package_slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    wrapped_selection = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_latex_insertion_events", x => x.id);
                    table.CheckConstraint("ck_insertion_event_kind", "token_kind IN ('command','environment','declaration','length','counter')");
                    table.CheckConstraint("ck_insertion_event_source", "source IN ('panel','palette','slash','package-modal')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_insertion_event_source_recent",
                table: "latex_insertion_events",
                columns: new[] { "source", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_insertion_event_token_recent",
                table: "latex_insertion_events",
                columns: new[] { "token_name", "token_kind", "token_package_slug", "created_at" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_insertion_event_user_recent",
                table: "latex_insertion_events",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true },
                filter: "user_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "latex_insertion_events");
        }
    }
}
