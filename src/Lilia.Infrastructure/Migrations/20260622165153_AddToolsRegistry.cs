using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddToolsRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tool_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    tool_slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    anon_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(16)", maxLength: 16, nullable: false),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_events", x => x.id);
                    table.CheckConstraint("ck_tool_event", "event IN ('view','use','result','signup','pay')");
                });

            migrationBuilder.CreateTable(
                name: "tools",
                columns: table => new
                {
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    tagline = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    seo_description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    input_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "text"),
                    output_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "source"),
                    engine = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    free_limit_per_day = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    free_size_cap_bytes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    cross_sell_label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tools", x => x.slug);
                    table.CheckConstraint("ck_tool_input_kind", "input_kind IN ('text','grid','file')");
                    table.CheckConstraint("ck_tool_output_kind", "output_kind IN ('source','pdf','image')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tool_event_funnel",
                table: "tool_events",
                columns: new[] { "tool_slug", "event", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tool_event_quota",
                table: "tool_events",
                columns: new[] { "tool_slug", "anon_id", "event", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tool_enabled",
                table: "tools",
                column: "enabled");

            migrationBuilder.Sql(@"
INSERT INTO tools (slug, title, tagline, seo_description, input_kind, output_kind, engine, free_limit_per_day, free_size_cap_bytes, cross_sell_label, enabled, sort_order, created_at, updated_at) VALUES
('doi-to-bibtex','DOI to BibTeX','Paste a DOI, ISBN, or URL and get clean BibTeX instantly.','Free DOI to BibTeX generator — paste a DOI and get a formatted BibTeX entry, ready for LaTeX.','text','source','doi',20,0,'Open in Lilia editor',true,1,now(),now()),
('latex-table','LaTeX Table Generator','Build a table in a grid and export clean booktabs LaTeX.','Free LaTeX table generator — build tables in a grid and export publication-grade booktabs LaTeX.','grid','source','table',0,0,'Open in Lilia editor',true,2,now(),now()),
('word-to-latex','Word to LaTeX','Convert a .docx to LaTeX, then refine it in Lilia.','Free Word to LaTeX converter — upload a .docx and get a LaTeX draft; open in Lilia for full-fidelity review.','file','source','word',3,5000000,'Open in Lilia to review',true,3,now(),now())
ON CONFLICT (slug) DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tool_events");

            migrationBuilder.DropTable(
                name: "tools");
        }
    }
}
