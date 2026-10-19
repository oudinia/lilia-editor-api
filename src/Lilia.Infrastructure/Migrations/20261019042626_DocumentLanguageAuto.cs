using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentLanguageAuto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "language",
                table: "documents",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "auto",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValue: "en");

            // "en" was every document's default, chosen or not: from now on the language is read from the
            // text unless set (DocumentLanguage). English text still reads as English. No trigger on
            // documents, so updated_at and the documents list order stay as they were.
            migrationBuilder.Sql("UPDATE documents SET language = 'auto' WHERE language = 'en';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "language",
                table: "documents",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "en",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValue: "auto");
            migrationBuilder.Sql("UPDATE documents SET language = 'en' WHERE language = 'auto';");
        }
    }
}
