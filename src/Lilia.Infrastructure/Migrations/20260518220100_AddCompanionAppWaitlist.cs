using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanionAppWaitlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "companion_app_waitlist",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "en"),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "banner"),
                    signed_up_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    notified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    unsubscribed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companion_app_waitlist", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_companion_app_waitlist_email",
                table: "companion_app_waitlist",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "IX_companion_app_waitlist_signed_up_at",
                table: "companion_app_waitlist",
                column: "signed_up_at",
                descending: new bool[0]);

            // Case-insensitive uniqueness on email — partial unique
            // index over LOWER(email) so "Foo@Bar.com" and "foo@bar.com"
            // can't both end up on the waitlist. Filtered on
            // unsubscribed_at IS NULL so a user who unsubscribed can
            // re-subscribe later.
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ux_companion_app_waitlist_email_lower
                ON companion_app_waitlist (LOWER(email))
                WHERE unsubscribed_at IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_companion_app_waitlist_email_lower;");
            migrationBuilder.DropTable(
                name: "companion_app_waitlist");
        }
    }
}
