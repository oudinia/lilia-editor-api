using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ParkCvDocuments : Migration
    {
        /// <summary>
        /// The timestamp this migration stamps onto the CVs it parks.
        ///
        /// A sentinel rather than now() so Down can undo exactly the rows this
        /// migration touched. A CV the author trashed themselves carries their
        /// own deleted_at and must stay trashed — un-deleting it would be this
        /// migration inventing a decision the author already made.
        /// </summary>
        private const string Sentinel = "2026-09-06 17:00:00+00";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CV is parked until the feature is picked up again (1.7% of the
            // corpus and falling — the same evidence that took it out of the
            // document picker in the web editor).
            //
            // Soft, not hard: deleted_at is the existing trash mechanism, so the
            // documents keep their blocks, versions and history and come back by
            // clearing one column. A DELETE would cascade and be unrecoverable,
            // and "until we pick it up much later" is not a reason to destroy an
            // author's work.
            migrationBuilder.Sql($@"
UPDATE documents
   SET deleted_at = TIMESTAMPTZ '{Sentinel}'
 WHERE lower(document_category) = 'cv'
   AND deleted_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
UPDATE documents
   SET deleted_at = NULL
 WHERE lower(document_category) = 'cv'
   AND deleted_at = TIMESTAMPTZ '{Sentinel}';");
        }
    }
}
