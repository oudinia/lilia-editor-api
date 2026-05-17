using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicLinkExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "link_expires_at",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "link_permission",
                table: "documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "view");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "link_expires_at",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "link_permission",
                table: "documents");
        }
    }
}
