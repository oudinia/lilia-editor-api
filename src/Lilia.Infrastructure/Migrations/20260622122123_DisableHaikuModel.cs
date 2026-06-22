using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DisableHaikuModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Haiku 4.5 dropped from the user-facing lineup — quality-first
            // editor keeps just Sonnet 4.6 (default) + Opus 4.8. Row kept
            // (disabled) so it can return for cheap background tasks later.
            migrationBuilder.Sql("UPDATE ai_models SET enabled=false, updated_at=now() WHERE id='claude-haiku-4-5';");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE ai_models SET enabled=true, updated_at=now() WHERE id='claude-haiku-4-5';");

        }
    }
}
