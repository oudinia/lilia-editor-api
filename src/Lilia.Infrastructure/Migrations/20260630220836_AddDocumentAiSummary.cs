using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentAiSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiSummary",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiSummaryGeneratedAt",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiSummary",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "AiSummaryGeneratedAt",
                table: "documents");
        }
    }
}
