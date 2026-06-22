using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiModelCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_models",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tier_label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "default"),
                    min_membership = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pro"),
                    credit_in_per_ktok = table.Column<decimal>(type: "numeric(10,4)", nullable: false, defaultValue: 0m),
                    credit_out_per_ktok = table.Column<decimal>(type: "numeric(10,4)", nullable: false, defaultValue: 0m),
                    context_window = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_output = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    supports_attachments = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    supports_vision = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    prompt_cache = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_models", x => x.id);
                    table.CheckConstraint("ck_ai_model_membership", "min_membership IN ('free','pro','team')");
                    table.CheckConstraint("ck_ai_model_provider", "provider IN ('anthropic','openai','google')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_model_enabled",
                table: "ai_models",
                column: "enabled");

            migrationBuilder.CreateIndex(
                name: "ux_ai_model_default",
                table: "ai_models",
                column: "is_default",
                unique: true,
                filter: "is_default");

            // Seed the initial model lineup. Sonnet 4.6 is the default; Opus is
            // pro-gated. Credit rates are placeholders (tuned when Phase 2 metering
            // lands). ON CONFLICT keeps this idempotent.
            migrationBuilder.Sql(@"
INSERT INTO ai_models (id, provider, display_name, tier_label, min_membership, credit_in_per_ktok, credit_out_per_ktok, context_window, max_output, supports_attachments, supports_vision, prompt_cache, is_default, enabled, sort_order, created_at, updated_at) VALUES
('claude-haiku-4-5','anthropic','Haiku 4.5','fast','free',0.2,1.0,200000,8192,true,true,true,false,true,1,now(),now()),
('claude-sonnet-4-6','anthropic','Sonnet 4.6','default','free',0.6,3.0,200000,16384,true,true,true,true,true,2,now(),now()),
('claude-opus-4-8','anthropic','Opus 4.8','premium','pro',3.0,15.0,200000,16384,true,true,true,false,true,3,now(),now())
ON CONFLICT (id) DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_models");
        }
    }
}
