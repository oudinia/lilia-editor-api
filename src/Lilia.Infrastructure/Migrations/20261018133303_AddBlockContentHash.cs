using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBlockContentHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "content_hash",
                table: "blocks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // A block's hash is the database's to keep (18 Oct): set before every insert and update,
            // whatever the column list, so no write path (EF, ExecuteUpdate, raw SQL, imports) can
            // leave it stale. A document's version is the hash of its blocks' hashes.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION set_block_content_hash() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    NEW.content_hash := encode(sha256(convert_to(
                        coalesce(NEW.type, '') || chr(1) || coalesce(NEW.content::text, '') || chr(1) || coalesce(NEW.metadata::text, ''),
                        'UTF8')), 'hex');
                    RETURN NEW;
                END $$;
                CREATE TRIGGER trg_blocks_content_hash BEFORE INSERT OR UPDATE ON blocks
                    FOR EACH ROW EXECUTE FUNCTION set_block_content_hash();
                -- Every existing block (the shape queue's trigger fires on content/type only: not here).
                UPDATE blocks SET content_hash = NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_blocks_content_hash ON blocks;
                DROP FUNCTION IF EXISTS set_block_content_hash();
                """);
            migrationBuilder.DropColumn(
                name: "content_hash",
                table: "blocks");
        }
    }
}
