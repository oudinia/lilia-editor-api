using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Credit rates from the models' real prices (3 Nov 2026, the owner: "ai allowance is too short"). The
    /// rates set when the catalog moved to Claude 5 copied the tiers they replaced (Sonnet 4.6, Opus 4.8)
    /// "until real per-token pricing is confirmed": Sonnet 5 was charged 1.8x its price, Opus 5 3.6x. A
    /// credit is $0.006 (the peg the plans were set with), so a rate is the price per million tokens / 6.
    /// AiArchitectPricing holds the same prices; a test checks the two agree.
    /// </summary>
    public partial class CreditRatesFromModelPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Price per million tokens (input/output) → credits per 1,000 tokens at $0.006 a credit.
            migrationBuilder.Sql(@"
UPDATE ai_models AS m SET credit_in_per_ktok = r.cin, credit_out_per_ktok = r.cout, updated_at = now()
FROM (VALUES
  ('claude-sonnet-5',   0.3333, 1.6667),  -- $2 / $10
  ('claude-opus-5',     0.8333, 4.1667),  -- $5 / $25
  ('claude-opus-4-8',   0.8333, 4.1667),  -- $5 / $25
  ('claude-sonnet-4-6', 0.5000, 2.5000),  -- $3 / $15
  ('claude-haiku-4-5',  0.1667, 0.8333)   -- $1 / $5
) AS r(id, cin, cout)
WHERE m.id = r.id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE ai_models AS m SET credit_in_per_ktok = r.cin, credit_out_per_ktok = r.cout, updated_at = now()
FROM (VALUES
  ('claude-sonnet-5', 0.6, 3.0), ('claude-opus-5', 3.0, 15.0), ('claude-opus-4-8', 3.0, 15.0),
  ('claude-sonnet-4-6', 0.6, 3.0), ('claude-haiku-4-5', 0.2, 1.0)
) AS r(id, cin, cout)
WHERE m.id = r.id;");
        }
    }
}
