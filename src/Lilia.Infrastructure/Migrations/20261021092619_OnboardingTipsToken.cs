using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OnboardingTipsToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tips_token",
                table: "user_onboarding",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_onboarding_tips_token",
                table: "user_onboarding",
                column: "tips_token",
                unique: true,
                filter: "tips_token IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_onboarding_tips_token",
                table: "user_onboarding");

            migrationBuilder.DropColumn(
                name: "tips_token",
                table: "user_onboarding");
        }
    }
}
