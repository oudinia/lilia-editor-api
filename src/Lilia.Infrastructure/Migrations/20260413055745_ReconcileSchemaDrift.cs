using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileSchemaDrift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tenant_id",
                table: "jobs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "raw_import_data",
                table: "import_review_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "formulas",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "help_category",
                table: "documents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "help_order",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "help_slug",
                table: "documents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_help_content",
                table: "documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_public_template",
                table: "documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_template",
                table: "documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "search_text",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "share_slug",
                table: "documents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "template_category",
                table: "documents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "template_description",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "template_name",
                table: "documents",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "template_thumbnail",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "template_usage_count",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_auto_save",
                table: "document_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "metadata",
                table: "blocks",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "path",
                table: "blocks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "blocks",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "draft");

            migrationBuilder.CreateTable(
                name: "block_previews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    block_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: true),
                    rendered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_block_previews", x => x.id);
                    table.ForeignKey(
                        name: "FK_block_previews_blocks_block_id",
                        column: x => x.block_id,
                        principalTable: "blocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_pending_invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    invited_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_pending_invites", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_pending_invites_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_document_pending_invites_users_invited_by",
                        column: x => x.invited_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "draft_blocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    content = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    tags = table.Column<string>(type: "jsonb", nullable: false),
                    is_favorite = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    usage_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_draft_blocks", x => x.id);
                    table.ForeignKey(
                        name: "FK_draft_blocks_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "general"),
                    message = table.Column<string>(type: "text", nullable: false),
                    page = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    block_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    block_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    document_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "new"),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    response = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback", x => x.id);
                    table.ForeignKey(
                        name: "FK_feedback_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                    table.ForeignKey(
                        name: "FK_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "studio_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    focused_block_id = table.Column<Guid>(type: "uuid", nullable: true),
                    layout = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    collapsed_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    pinned_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    view_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "tree"),
                    last_accessed = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_studio_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_studio_sessions_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_blocks_document_id_path",
                table: "blocks",
                columns: new[] { "document_id", "path" });

            migrationBuilder.CreateIndex(
                name: "IX_blocks_document_id_status",
                table: "blocks",
                columns: new[] { "document_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_block_previews_block_id",
                table: "block_previews",
                column: "block_id");

            migrationBuilder.CreateIndex(
                name: "IX_block_previews_block_id_format",
                table: "block_previews",
                columns: new[] { "block_id", "format" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_pending_invites_document_id_email",
                table: "document_pending_invites",
                columns: new[] { "document_id", "email" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_document_pending_invites_email",
                table: "document_pending_invites",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "IX_document_pending_invites_invited_by",
                table: "document_pending_invites",
                column: "invited_by");

            migrationBuilder.CreateIndex(
                name: "IX_draft_blocks_user_id",
                table: "draft_blocks",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_draft_blocks_user_id_category",
                table: "draft_blocks",
                columns: new[] { "user_id", "category" });

            migrationBuilder.CreateIndex(
                name: "IX_draft_blocks_user_id_is_favorite",
                table: "draft_blocks",
                columns: new[] { "user_id", "is_favorite" });

            migrationBuilder.CreateIndex(
                name: "IX_draft_blocks_user_id_type",
                table: "draft_blocks",
                columns: new[] { "user_id", "type" });

            migrationBuilder.CreateIndex(
                name: "IX_feedback_status_created_at",
                table: "feedback",
                columns: new[] { "status", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_feedback_type",
                table: "feedback",
                column: "type");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_user_id",
                table: "feedback",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_created_at",
                table: "notifications",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_user_id",
                table: "notifications",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_user_id_is_read",
                table: "notifications",
                columns: new[] { "user_id", "is_read" });

            migrationBuilder.CreateIndex(
                name: "IX_studio_sessions_document_id",
                table: "studio_sessions",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_studio_sessions_user_id_document_id",
                table: "studio_sessions",
                columns: new[] { "user_id", "document_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "block_previews");

            migrationBuilder.DropTable(
                name: "document_pending_invites");

            migrationBuilder.DropTable(
                name: "draft_blocks");

            migrationBuilder.DropTable(
                name: "feedback");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "studio_sessions");

            migrationBuilder.DropIndex(
                name: "IX_blocks_document_id_path",
                table: "blocks");

            migrationBuilder.DropIndex(
                name: "IX_blocks_document_id_status",
                table: "blocks");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "raw_import_data",
                table: "import_review_sessions");

            migrationBuilder.DropColumn(
                name: "version",
                table: "formulas");

            migrationBuilder.DropColumn(
                name: "help_category",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "help_order",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "help_slug",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "is_help_content",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "is_public_template",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "is_template",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "search_text",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "share_slug",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "template_category",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "template_description",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "template_name",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "template_thumbnail",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "template_usage_count",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "is_auto_save",
                table: "document_versions");

            migrationBuilder.DropColumn(
                name: "metadata",
                table: "blocks");

            migrationBuilder.DropColumn(
                name: "path",
                table: "blocks");

            migrationBuilder.DropColumn(
                name: "status",
                table: "blocks");
        }
    }
}
