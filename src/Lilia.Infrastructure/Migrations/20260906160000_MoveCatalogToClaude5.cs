using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveCatalogToClaude5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Lineup moves to the Claude 5 family: Sonnet 5 as the default that
            // Ask Lilia runs on, Opus 5 as the pro-tier premium option.
            //
            // The 4.x rows are disabled, not deleted — same call as the Haiku 4.5
            // migration. AiCatalogService.Get() resolves disabled rows so credit
            // costs on historical usage still price correctly, while a disabled
            // model "never appears or resolves", so anyone whose stored preference
            // was Sonnet 4.6 falls through to the new default rather than pinning
            // a retired id.
            //
            // is_default is cleared before the insert: exactly one row is meant to
            // carry it, and doing it in this order means the table is never
            // momentarily without a default.
            migrationBuilder.Sql("UPDATE ai_models SET is_default=false, updated_at=now() WHERE is_default=true;");

            // Credit rates mirror the tier each model replaces (Sonnet 4.6 →
            // 0.6/3.0, Opus 4.8 → 3.0/15.0) rather than inventing figures. Revisit
            // when real per-token pricing is confirmed.
            migrationBuilder.Sql(@"
INSERT INTO ai_models (id, provider, display_name, tier_label, min_membership, credit_in_per_ktok, credit_out_per_ktok, context_window, max_output, supports_attachments, supports_vision, prompt_cache, is_default, enabled, sort_order, created_at, updated_at) VALUES
('claude-sonnet-5','anthropic','Sonnet 5','default','free',0.6,3.0,200000,16384,true,true,true,true,true,2,now(),now()),
('claude-opus-5','anthropic','Opus 5','premium','pro',3.0,15.0,200000,16384,true,true,true,false,true,3,now(),now())
ON CONFLICT (id) DO UPDATE SET
  display_name=EXCLUDED.display_name, tier_label=EXCLUDED.tier_label,
  min_membership=EXCLUDED.min_membership, is_default=EXCLUDED.is_default,
  enabled=true, sort_order=EXCLUDED.sort_order, updated_at=now();");

            migrationBuilder.Sql(
                "UPDATE ai_models SET enabled=false, is_default=false, sort_order=sort_order+10, updated_at=now() " +
                "WHERE id IN ('claude-sonnet-4-6','claude-opus-4-8');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM ai_models WHERE id IN ('claude-sonnet-5','claude-opus-5');");
            migrationBuilder.Sql(
                "UPDATE ai_models SET enabled=true, sort_order=sort_order-10, updated_at=now() " +
                "WHERE id IN ('claude-sonnet-4-6','claude-opus-4-8');");
            migrationBuilder.Sql("UPDATE ai_models SET is_default=true, updated_at=now() WHERE id='claude-sonnet-4-6';");
        }
    }
}
