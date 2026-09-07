using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentCurrentVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Which version the document currently reflects.
            //
            // Restoring used to append "Restored from version N" so the newest
            // row would equal the document. That kept the timeline honest but
            // read backwards: you asked to go back and the history grew. The
            // marker moves instead, which is what git checkout does — move a
            // pointer, create nothing.
            //
            // Deliberately no foreign key. A version can be deleted while a
            // document points at it, and the read path already validates the
            // pointer against the document's content, so a dangling id resolves
            // to "not on any version" — which is the truth — rather than
            // blocking the delete or cascading into one.
            migrationBuilder.Sql(
                "ALTER TABLE documents ADD COLUMN IF NOT EXISTS current_version_id uuid NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE documents DROP COLUMN IF EXISTS current_version_id;");
        }
    }
}
