using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Claude Haiku 5.5 in the catalog (3 Nov 2026), for Ask Lilia's "fast" skills (AI:Tiers): priced, not
    /// offered in the model picker (enabled = false). Its rate is the price above 100,000 prompt tokens
    /// ($0.50 / $2.50 per million, five times the base): Ask Lilia often sends more, and credits must never
    /// undercharge. At $0.006 a credit: 0.0833 / 0.4167 per 1,000 tokens.
    /// </summary>
    public partial class AddHaiku55ForFastSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO ai_models (id, provider, display_name, tier_label, min_membership, credit_in_per_ktok, credit_out_per_ktok, context_window, max_output, supports_attachments, supports_vision, prompt_cache, is_default, enabled, sort_order, created_at, updated_at) VALUES
('claude-haiku-5-5','anthropic','Haiku 5.5','fast','free',0.0833,0.4167,1000000,16384,true,true,true,false,false,1,now(),now())
ON CONFLICT (id) DO UPDATE SET credit_in_per_ktok=EXCLUDED.credit_in_per_ktok, credit_out_per_ktok=EXCLUDED.credit_out_per_ktok, updated_at=now();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM ai_models WHERE id = 'claude-haiku-5-5';");
        }
    }
}
