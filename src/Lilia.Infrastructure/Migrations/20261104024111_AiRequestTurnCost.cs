using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AiRequestTurnCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cache_read_tokens",
                table: "ai_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "cache_write_tokens",
                table: "ai_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "cost_usd",
                table: "ai_requests",
                type: "numeric(12,6)",
                precision: 12,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "largest_prompt_tokens",
                table: "ai_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rounds",
                table: "ai_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "skill",
                table: "ai_requests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cache_read_tokens",
                table: "ai_requests");

            migrationBuilder.DropColumn(
                name: "cache_write_tokens",
                table: "ai_requests");

            migrationBuilder.DropColumn(
                name: "cost_usd",
                table: "ai_requests");

            migrationBuilder.DropColumn(
                name: "largest_prompt_tokens",
                table: "ai_requests");

            migrationBuilder.DropColumn(
                name: "rounds",
                table: "ai_requests");

            migrationBuilder.DropColumn(
                name: "skill",
                table: "ai_requests");
        }
    }
}
