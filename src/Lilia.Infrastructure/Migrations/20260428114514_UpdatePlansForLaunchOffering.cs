using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePlansForLaunchOffering : Migration
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
                sql: "slug IN ('free','beta','conversion','student','pro','team','epub','compliance_pro','enterprise')");

            // Launch-offering plan setup. Existing 'free' / 'pro' rows
            // updated in place; new 'beta' + 'conversion' rows
            // inserted; non-launch SKUs (student / team / epub /
            // compliance_pro / enterprise) flipped is_active=false so
            // they don't surface in the pricing UI but stay in DB
            // for post-launch revival.
            migrationBuilder.Sql(@"
-- Free: 3 docs, 3 imports/mo, no AI, basic exports (clipped enforced
-- in API layer reading caps).
UPDATE plans SET
  caps = '{""maxDocs"":3,""maxImportsPerMonth"":3,""aiCreditsPerMonth"":0,""maxTeamSeats"":0,""exportPageLimit"":10}'::jsonb,
  features = '[""editor"",""export_pdf"",""export_latex""]'::jsonb,
  is_active = true,
  updated_at = NOW()
WHERE slug = 'free';

-- Pro: $10.99/mo (no annual yet — let Free + monthly signal stabilise
-- first), unlimited docs/imports/exports, bibliography lookup,
-- validation, share links. AI deactivated for launch.
UPDATE plans SET
  monthly_price = 10.99,
  yearly_price = NULL,
  caps = '{""maxDocs"":-1,""maxImportsPerMonth"":-1,""aiCreditsPerMonth"":0,""maxTeamSeats"":0,""exportPageLimit"":-1}'::jsonb,
  features = '[""editor"",""export_pdf"",""export_latex"",""export_docx"",""export_html"",""export_markdown"",""bibliography_lookup"",""validation"",""share_links"",""templates"",""versions""]'::jsonb,
  display_name = 'Pro',
  is_active = true,
  updated_at = NOW()
WHERE slug = 'pro';

-- Beta: open-beta tier — all of Pro UNLOCKED for the 3-4 week beta
-- window. AI stays off (no token cost). Migrated to free + 30-day
-- Pro coupon at paid launch.
INSERT INTO plans (id, slug, display_name, monthly_price, yearly_price, caps, features, is_active, created_at, updated_at)
VALUES (
  gen_random_uuid(), 'beta', 'Beta',
  NULL, NULL,
  '{""maxDocs"":-1,""maxImportsPerMonth"":-1,""aiCreditsPerMonth"":0,""maxTeamSeats"":0,""exportPageLimit"":-1}'::jsonb,
  '[""editor"",""export_pdf"",""export_latex"",""export_docx"",""export_html"",""export_markdown"",""bibliography_lookup"",""validation"",""share_links"",""templates"",""versions""]'::jsonb,
  true, NOW(), NOW()
)
ON CONFLICT (slug) DO UPDATE SET
  caps = EXCLUDED.caps,
  features = EXCLUDED.features,
  is_active = true,
  updated_at = NOW();

-- Conversion: $7.99 one-time per document. Caps applied per-purchase
-- via separate purchase row → user_plan link in lilia-cloud webhook.
INSERT INTO plans (id, slug, display_name, monthly_price, yearly_price, caps, features, is_active, created_at, updated_at)
VALUES (
  gen_random_uuid(), 'conversion', 'Conversion',
  7.99, NULL,
  '{""maxDocs"":1,""maxImportsPerMonth"":-1,""aiCreditsPerMonth"":0,""maxTeamSeats"":0,""exportPageLimit"":-1,""retentionDays"":30}'::jsonb,
  '[""export_pdf"",""export_latex"",""export_docx"",""export_html"",""export_markdown""]'::jsonb,
  true, NOW(), NOW()
)
ON CONFLICT (slug) DO UPDATE SET
  monthly_price = EXCLUDED.monthly_price,
  caps = EXCLUDED.caps,
  features = EXCLUDED.features,
  is_active = true,
  updated_at = NOW();

-- Non-launch SKUs: deactivated for now, kept in DB for post-launch revival.
UPDATE plans SET is_active = false, updated_at = NOW()
WHERE slug IN ('student','team','epub','compliance_pro','enterprise');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_plan_slug",
                table: "plans");

            migrationBuilder.AddCheckConstraint(
                name: "ck_plan_slug",
                table: "plans",
                sql: "slug IN ('free','student','pro','team','epub','compliance_pro','enterprise')");
        }
    }
}
