using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLatexCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "latex_document_classes",
                columns: table => new
                {
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    coverage_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    default_engine = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    required_packages = table.Column<string>(type: "jsonb", nullable: true),
                    shim_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_latex_document_classes", x => x.slug);
                    table.CheckConstraint("ck_latex_class_category", "category IN ('cv','article','report','book','presentation','letter','memoir','other')");
                    table.CheckConstraint("ck_latex_class_coverage", "coverage_level IN ('full','partial','shimmed','none','unsupported')");
                    table.CheckConstraint("ck_latex_class_engine", "default_engine IS NULL OR default_engine IN ('pdflatex','xelatex','lualatex')");
                });

            migrationBuilder.CreateTable(
                name: "latex_packages",
                columns: table => new
                {
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    coverage_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    coverage_notes = table.Column<string>(type: "text", nullable: true),
                    ctan_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_latex_packages", x => x.slug);
                    table.CheckConstraint("ck_latex_package_category", "category IN ('math','graphics','bibliography','layout','language','font','cv','presentation','code','table','reference','utility')");
                    table.CheckConstraint("ck_latex_package_coverage", "coverage_level IN ('full','partial','shimmed','none','unsupported')");
                });

            migrationBuilder.CreateTable(
                name: "latex_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    package_slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    arity = table.Column<int>(type: "integer", nullable: true),
                    optional_arity = table.Column<int>(type: "integer", nullable: true),
                    expects_body = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    semantic_category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    maps_to_block_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    coverage_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    alias_of = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_latex_tokens", x => x.id);
                    table.CheckConstraint("ck_latex_token_coverage", "coverage_level IN ('full','partial','shimmed','none','unsupported')");
                    table.CheckConstraint("ck_latex_token_kind", "kind IN ('command','environment','declaration','length','counter')");
                    table.ForeignKey(
                        name: "FK_latex_tokens_latex_packages_package_slug",
                        column: x => x.package_slug,
                        principalTable: "latex_packages",
                        principalColumn: "slug",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_latex_tokens_latex_tokens_alias_of",
                        column: x => x.alias_of,
                        principalTable: "latex_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "latex_token_usage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    token_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    first_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_latex_token_usage", x => x.id);
                    table.ForeignKey(
                        name: "FK_latex_token_usage_import_review_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "import_review_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_latex_token_usage_latex_tokens_token_id",
                        column: x => x.token_id,
                        principalTable: "latex_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_latex_class_category",
                table: "latex_document_classes",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_latex_package_category",
                table: "latex_packages",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_latex_package_coverage",
                table: "latex_packages",
                column: "coverage_level");

            migrationBuilder.CreateIndex(
                name: "ix_latex_token_usage_last_seen",
                table: "latex_token_usage",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "ix_latex_token_usage_session",
                table: "latex_token_usage",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ux_latex_token_usage_token_session",
                table: "latex_token_usage",
                columns: new[] { "token_id", "session_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_latex_token_coverage",
                table: "latex_tokens",
                column: "coverage_level");

            migrationBuilder.CreateIndex(
                name: "ix_latex_token_name",
                table: "latex_tokens",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_latex_token_package",
                table: "latex_tokens",
                column: "package_slug",
                filter: "package_slug IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_latex_tokens_alias_of",
                table: "latex_tokens",
                column: "alias_of");

            migrationBuilder.CreateIndex(
                name: "ux_latex_token_name_kind_pkg",
                table: "latex_tokens",
                columns: new[] { "name", "kind", "package_slug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "latex_document_classes");

            migrationBuilder.DropTable(
                name: "latex_token_usage");

            migrationBuilder.DropTable(
                name: "latex_tokens");

            migrationBuilder.DropTable(
                name: "latex_packages");
        }
    }
}
