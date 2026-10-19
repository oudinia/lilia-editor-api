using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NewDocumentsLanguageAuto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Documents created on 19 Oct after DocumentLanguageAuto still got "en" from the create
            // request's old default, not from anyone's choice: they read their language from the text too.
            migrationBuilder.Sql("UPDATE documents SET language = 'auto' WHERE language = 'en' AND created_at >= '2026-10-19';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
