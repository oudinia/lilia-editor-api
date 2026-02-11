using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidatedSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ban_expires",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ban_reason",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "banned",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "display_username",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "locale",
                table: "users",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "onboarding_complete",
                table: "users",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payments_customer_id",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "two_factor_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "default_export_format",
                table: "user_preferences",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "PDF");

            migrationBuilder.AddColumn<string>(
                name: "default_language",
                table: "user_preferences",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<JsonDocument>(
                name: "export_options",
                table: "user_preferences",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "preview_enabled",
                table: "user_preferences",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "sidebar_collapsed",
                table: "user_preferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "direction",
                table: "jobs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "error_details",
                table: "jobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "expires_at",
                table: "jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "input_file_key",
                table: "jobs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "input_file_size",
                table: "jobs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_retries",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "options",
                table: "jobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "output_file_key",
                table: "jobs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "retry_count",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "started_at",
                table: "jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_auto_saved_at",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "draft");

            migrationBuilder.AddColumn<string>(
                name: "created_by",
                table: "blocks",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "content_hash",
                table: "assets",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_accessed_at",
                table: "assets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "s3_bucket",
                table: "assets",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "usage_count",
                table: "assets",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "user_id",
                table: "assets",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    account_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    provider_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    access_token = table.Column<string>(type: "text", nullable: true),
                    refresh_token = table.Column<string>(type: "text", nullable: true),
                    id_token = table.Column<string>(type: "text", nullable: true),
                    access_token_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    refresh_token_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    scope = table.Column<string>(type: "text", nullable: true),
                    password = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    block_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    resolved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comments", x => x.id);
                    table.ForeignKey(
                        name: "FK_comments_blocks_block_id",
                        column: x => x.block_id,
                        principalTable: "blocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_comments_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_comments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversion_audits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    details = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    duration_ms = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversion_audits", x => x.id);
                    table.ForeignKey(
                        name: "FK_conversion_audits_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_conversion_audits_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    blocks_snapshot = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_snapshots_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_document_snapshots_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "import_review_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    document_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "in_progress"),
                    original_warnings = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_review_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_import_review_sessions_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_import_review_sessions_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_import_review_sessions_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    slug = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    logo = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<string>(type: "text", nullable: true),
                    payments_customer_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organizations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "passkeys",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    public_key = table.Column<string>(type: "text", nullable: false),
                    credential_id = table.Column<string>(type: "text", nullable: false),
                    counter = table.Column<int>(type: "integer", nullable: false),
                    device_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    backed_up = table.Column<bool>(type: "boolean", nullable: false),
                    transports = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    aaguid = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_passkeys", x => x.id);
                    table.ForeignKey(
                        name: "FK_passkeys_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    token = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    impersonated_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    active_organization_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sync_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sync_version = table.Column<int>(type: "integer", nullable: false),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_sync_history_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sync_history_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "two_factors",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    secret = table.Column<string>(type: "text", nullable: false),
                    backup_codes = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_two_factors", x => x.id);
                    table.ForeignKey(
                        name: "FK_two_factors_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "verifications",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    identifier = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "comment_replies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    comment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comment_replies", x => x.id);
                    table.ForeignKey(
                        name: "FK_comment_replies_comments_comment_id",
                        column: x => x.comment_id,
                        principalTable: "comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_comment_replies_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_block_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    block_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    resolved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    resolved_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_block_comments", x => x.id);
                    table.ForeignKey(
                        name: "FK_import_block_comments_import_review_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "import_review_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_import_block_comments_users_resolved_by",
                        column: x => x.resolved_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_import_block_comments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_block_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    block_index = table.Column<int>(type: "integer", nullable: false),
                    block_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    reviewed_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    original_content = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    original_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    current_content = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    current_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    confidence = table.Column<int>(type: "integer", nullable: true),
                    warnings = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_block_reviews", x => x.id);
                    table.ForeignKey(
                        name: "FK_import_block_reviews_import_review_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "import_review_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_import_block_reviews_users_reviewed_by",
                        column: x => x.reviewed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "import_review_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    block_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    details = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_review_activities", x => x.id);
                    table.ForeignKey(
                        name: "FK_import_review_activities_import_review_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "import_review_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_import_review_activities_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_review_collaborators",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "reviewer"),
                    invited_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    invited_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    last_active_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_review_collaborators", x => x.id);
                    table.ForeignKey(
                        name: "FK_import_review_collaborators_import_review_sessions_session_~",
                        column: x => x.session_id,
                        principalTable: "import_review_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_import_review_collaborators_users_invited_by",
                        column: x => x.invited_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_import_review_collaborators_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_chats",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    organization_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    messages = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_chats", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_chats_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ai_chats_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invitations",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    organization_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    inviter_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitations", x => x.id);
                    table.ForeignKey(
                        name: "FK_invitations_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_invitations_users_inviter_id",
                        column: x => x.inviter_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organization_members",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    organization_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "member"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_members", x => x.id);
                    table.ForeignKey(
                        name: "FK_organization_members_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_organization_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchases",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    organization_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    subscription_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    product_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchases", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchases_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_purchases_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                table: "users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_blocks_created_by",
                table: "blocks",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_assets_content_hash",
                table: "assets",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "IX_assets_user_id",
                table: "assets",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_user_id",
                table: "accounts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_chats_organization_id",
                table: "ai_chats",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_chats_user_id",
                table: "ai_chats",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_comment_replies_comment_id",
                table: "comment_replies",
                column: "comment_id");

            migrationBuilder.CreateIndex(
                name: "IX_comment_replies_user_id",
                table: "comment_replies",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_comments_block_id",
                table: "comments",
                column: "block_id");

            migrationBuilder.CreateIndex(
                name: "IX_comments_document_id",
                table: "comments",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_comments_user_id",
                table: "comments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_conversion_audits_job_id",
                table: "conversion_audits",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "IX_conversion_audits_user_id",
                table: "conversion_audits",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_conversion_audits_user_id_timestamp",
                table: "conversion_audits",
                columns: new[] { "user_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_document_snapshots_created_by",
                table: "document_snapshots",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_document_snapshots_document_id",
                table: "document_snapshots",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_block_comments_resolved_by",
                table: "import_block_comments",
                column: "resolved_by");

            migrationBuilder.CreateIndex(
                name: "IX_import_block_comments_session_id",
                table: "import_block_comments",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_block_comments_session_id_block_id",
                table: "import_block_comments",
                columns: new[] { "session_id", "block_id" });

            migrationBuilder.CreateIndex(
                name: "IX_import_block_comments_user_id",
                table: "import_block_comments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_block_reviews_reviewed_by",
                table: "import_block_reviews",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "IX_import_block_reviews_session_id",
                table: "import_block_reviews",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_block_reviews_session_id_block_id",
                table: "import_block_reviews",
                columns: new[] { "session_id", "block_id" });

            migrationBuilder.CreateIndex(
                name: "IX_import_block_reviews_session_id_status",
                table: "import_block_reviews",
                columns: new[] { "session_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_import_review_activities_session_id",
                table: "import_review_activities",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_review_activities_session_id_created_at",
                table: "import_review_activities",
                columns: new[] { "session_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_import_review_activities_user_id",
                table: "import_review_activities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_review_collaborators_invited_by",
                table: "import_review_collaborators",
                column: "invited_by");

            migrationBuilder.CreateIndex(
                name: "IX_import_review_collaborators_session_id",
                table: "import_review_collaborators",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_review_collaborators_session_id_user_id",
                table: "import_review_collaborators",
                columns: new[] { "session_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_import_review_collaborators_user_id",
                table: "import_review_collaborators",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_review_sessions_document_id",
                table: "import_review_sessions",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_review_sessions_job_id",
                table: "import_review_sessions",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_review_sessions_owner_id",
                table: "import_review_sessions",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_email",
                table: "invitations",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_inviter_id",
                table: "invitations",
                column: "inviter_id");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_organization_id",
                table: "invitations",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_organization_members_organization_id",
                table: "organization_members",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_organization_members_organization_id_user_id",
                table: "organization_members",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_members_user_id",
                table: "organization_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_organizations_slug",
                table: "organizations",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_passkeys_credential_id",
                table: "passkeys",
                column: "credential_id");

            migrationBuilder.CreateIndex(
                name: "IX_passkeys_user_id",
                table: "passkeys",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchases_organization_id",
                table: "purchases",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchases_subscription_id",
                table: "purchases",
                column: "subscription_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchases_user_id",
                table: "purchases",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_token",
                table: "sessions",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sessions_user_id",
                table: "sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_sync_history_document_id",
                table: "sync_history",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_sync_history_user_id",
                table: "sync_history",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_two_factors_secret",
                table: "two_factors",
                column: "secret");

            migrationBuilder.CreateIndex(
                name: "IX_two_factors_user_id",
                table: "two_factors",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_verifications_identifier",
                table: "verifications",
                column: "identifier");

            migrationBuilder.AddForeignKey(
                name: "FK_assets_users_user_id",
                table: "assets",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_blocks_users_created_by",
                table: "blocks",
                column: "created_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_assets_users_user_id",
                table: "assets");

            migrationBuilder.DropForeignKey(
                name: "FK_blocks_users_created_by",
                table: "blocks");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "ai_chats");

            migrationBuilder.DropTable(
                name: "comment_replies");

            migrationBuilder.DropTable(
                name: "conversion_audits");

            migrationBuilder.DropTable(
                name: "document_snapshots");

            migrationBuilder.DropTable(
                name: "import_block_comments");

            migrationBuilder.DropTable(
                name: "import_block_reviews");

            migrationBuilder.DropTable(
                name: "import_review_activities");

            migrationBuilder.DropTable(
                name: "import_review_collaborators");

            migrationBuilder.DropTable(
                name: "invitations");

            migrationBuilder.DropTable(
                name: "organization_members");

            migrationBuilder.DropTable(
                name: "passkeys");

            migrationBuilder.DropTable(
                name: "purchases");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "sync_history");

            migrationBuilder.DropTable(
                name: "two_factors");

            migrationBuilder.DropTable(
                name: "verifications");

            migrationBuilder.DropTable(
                name: "comments");

            migrationBuilder.DropTable(
                name: "import_review_sessions");

            migrationBuilder.DropTable(
                name: "organizations");

            migrationBuilder.DropIndex(
                name: "IX_users_username",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_blocks_created_by",
                table: "blocks");

            migrationBuilder.DropIndex(
                name: "IX_assets_content_hash",
                table: "assets");

            migrationBuilder.DropIndex(
                name: "IX_assets_user_id",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "ban_expires",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ban_reason",
                table: "users");

            migrationBuilder.DropColumn(
                name: "banned",
                table: "users");

            migrationBuilder.DropColumn(
                name: "display_username",
                table: "users");

            migrationBuilder.DropColumn(
                name: "locale",
                table: "users");

            migrationBuilder.DropColumn(
                name: "onboarding_complete",
                table: "users");

            migrationBuilder.DropColumn(
                name: "payments_customer_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "role",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "username",
                table: "users");

            migrationBuilder.DropColumn(
                name: "default_export_format",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "default_language",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "export_options",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "preview_enabled",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "sidebar_collapsed",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "direction",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "error_details",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "input_file_key",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "input_file_size",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "max_retries",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "options",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "output_file_key",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "retry_count",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "started_at",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "last_auto_saved_at",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "status",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "created_by",
                table: "blocks");

            migrationBuilder.DropColumn(
                name: "content_hash",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "last_accessed_at",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "s3_bucket",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "usage_count",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "assets");
        }
    }
}
