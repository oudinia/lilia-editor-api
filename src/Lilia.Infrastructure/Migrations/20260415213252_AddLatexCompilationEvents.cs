using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLatexCompilationEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LaTeXCompilationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    BlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    BlockType = table.Column<string>(type: "text", nullable: true),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorRaw = table.Column<string>(type: "text", nullable: true),
                    ErrorCategory = table.Column<string>(type: "text", nullable: true),
                    ErrorToken = table.Column<string>(type: "text", nullable: true),
                    ErrorLine = table.Column<int>(type: "integer", nullable: true),
                    WarningCount = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LaTeXCompilationEvents", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LaTeXCompilationEvents");
        }
    }
}
