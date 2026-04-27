using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowTypstSourceFormatInTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 11 of the pre-launch Typst plan: PreviewRenderService
            // emits silent_fallback events with source_format='typst'
            // when the Typst path can't render a document and we fall
            // through to pdflatex. The original constraint shipped
            // without 'typst' in the allowed set; events were being
            // rolled back at flush time. Extending the closed
            // vocabulary here is the right migration path per CLAUDE.md
            // (no hand-rolled SQL files).
            migrationBuilder.Sql(@"
ALTER TABLE import_telemetry_events
DROP CONSTRAINT IF EXISTS ck_telemetry_source_format;

ALTER TABLE import_telemetry_events
ADD CONSTRAINT ck_telemetry_source_format
CHECK (source_format IN ('latex','docx','epub','pdf','lml','overleaf-zip','typst'));
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE import_telemetry_events
DROP CONSTRAINT IF EXISTS ck_telemetry_source_format;

ALTER TABLE import_telemetry_events
ADD CONSTRAINT ck_telemetry_source_format
CHECK (source_format IN ('latex','docx','epub','pdf','lml','overleaf-zip'));
");
        }
    }
}
