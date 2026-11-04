using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// The paid plans as decided on 4 Nov 2026: Pro at $10.99 a month or $99 a year with 500 Ask Lilia
    /// credits (was 800), and Pro Plus at $19.99 a month with 1,500, everything else as Pro. Its yearly
    /// price waits on the owner. Beta keeps 800. No one pays yet (no Stripe price on any plan), so lowering
    /// Pro's allowance changes nobody's subscription.
    /// </summary>
    public partial class PlansProPlusAndProAllowance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_plan_slug",
                table: "plans");

            migrationBuilder.AddCheckConstraint(
                name: "ck_plan_slug",
                table: "plans",
                sql: "slug IN ('free','beta','conversion','student','pro','pro_plus','team','epub','compliance_pro','enterprise')");

            migrationBuilder.Sql(@"
UPDATE plans SET
  caps = jsonb_set(caps, '{aiCreditsPerMonth}', '500'),
  yearly_price = 99.00,
  updated_at = NOW()
WHERE slug = 'pro';

INSERT INTO plans (id, slug, display_name, monthly_price, yearly_price, caps, features, is_active, created_at, updated_at)
SELECT gen_random_uuid(), 'pro_plus', 'Pro Plus', 19.99, NULL,
       jsonb_set(caps, '{aiCreditsPerMonth}', '1500'), features, true, NOW(), NOW()
FROM plans WHERE slug = 'pro'
ON CONFLICT (slug) DO NOTHING;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM plans WHERE slug = 'pro_plus' AND NOT EXISTS (SELECT 1 FROM user_plans up WHERE up.plan_id = plans.id);

UPDATE plans SET
  caps = jsonb_set(caps, '{aiCreditsPerMonth}', '800'),
  yearly_price = NULL,
  updated_at = NOW()
WHERE slug = 'pro';
");

            migrationBuilder.DropCheckConstraint(
                name: "ck_plan_slug",
                table: "plans");

            migrationBuilder.AddCheckConstraint(
                name: "ck_plan_slug",
                table: "plans",
                sql: "slug IN ('free','beta','conversion','student','pro','team','epub','compliance_pro','enterprise')");
        }
    }
}
