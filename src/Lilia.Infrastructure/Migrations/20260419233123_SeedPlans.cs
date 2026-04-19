using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Seed the 7 plan SKUs. Caps JSON keys must match what
            // EntitlementService.ReadCap looks up (maxDocs,
            // maxImportsPerMonth, aiCreditsPerMonth, maxTeamSeats).
            // -1 means unlimited.
            //
            // Raw SQL with ON CONFLICT so the migration is idempotent —
            // running it on a DB that already has these rows is a no-op.
            // We deliberately don't delete on Down because user_plans FK-
            // references plan rows; deleting breaks referential integrity.
            migrationBuilder.Sql(@"
INSERT INTO plans (slug, display_name, monthly_price, yearly_price, caps, features, is_active) VALUES
('free',            'Free',              NULL,   NULL,   '{""maxDocs"":3,""maxImportsPerMonth"":1,""aiCreditsPerMonth"":0,""maxTeamSeats"":0}'::jsonb,
 '[""editor"",""export_pdf"",""export_latex""]'::jsonb, true),
('student',         'Student',           6.00,   49.00,  '{""maxDocs"":-1,""maxImportsPerMonth"":10,""aiCreditsPerMonth"":50,""maxTeamSeats"":0}'::jsonb,
 '[""editor"",""export_pdf"",""export_latex"",""export_docx"",""ai"",""templates""]'::jsonb, true),
('pro',             'Pro',               19.00,  149.00, '{""maxDocs"":-1,""maxImportsPerMonth"":-1,""aiCreditsPerMonth"":200,""maxTeamSeats"":0}'::jsonb,
 '[""editor"",""export_pdf"",""export_latex"",""export_docx"",""ai"",""templates"",""desktop"",""versions""]'::jsonb, true),
('team',            'Team',              15.00,  120.00, '{""maxDocs"":-1,""maxImportsPerMonth"":-1,""aiCreditsPerMonth"":500,""maxTeamSeats"":-1}'::jsonb,
 '[""editor"",""export_pdf"",""export_latex"",""export_docx"",""ai"",""templates"",""desktop"",""versions"",""team""]'::jsonb, true),
('epub',            'ePub Studio',       9.00,   72.00,  '{""maxDocs"":5,""maxImportsPerMonth"":-1,""aiCreditsPerMonth"":0,""maxTeamSeats"":0}'::jsonb,
 '[""epub_studio"",""export_pdf"",""export_epub""]'::jsonb, true),
('compliance_pro',  'Compliance Pro',    29.00,  232.00, '{""maxDocs"":-1,""maxImportsPerMonth"":-1,""aiCreditsPerMonth"":100,""maxTeamSeats"":3}'::jsonb,
 '[""editor"",""export_pdf"",""export_latex"",""compliance_studio"",""templates"",""ai""]'::jsonb, true),
('enterprise',      'Enterprise',        NULL,   NULL,   '{""maxDocs"":-1,""maxImportsPerMonth"":-1,""aiCreditsPerMonth"":-1,""maxTeamSeats"":-1}'::jsonb,
 '[""editor"",""export_pdf"",""export_latex"",""export_docx"",""ai"",""templates"",""desktop"",""versions"",""team"",""api_access"",""sso""]'::jsonb, true)
ON CONFLICT (slug) DO NOTHING;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Safe down: deactivate instead of delete — user_plans FK-references these rows.
            migrationBuilder.Sql(@"UPDATE plans SET is_active = false WHERE slug IN
                ('free','student','pro','team','epub','compliance_pro','enterprise');");
        }
    }
}
