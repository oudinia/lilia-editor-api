using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFromMyDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_hides",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hidden_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_hides", x => new { x.user_id, x.document_id });
                    table.ForeignKey(
                        name: "FK_document_hides_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_hides_document_id",
                table: "document_hides",
                column: "document_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_hides");
        }
    }
}
