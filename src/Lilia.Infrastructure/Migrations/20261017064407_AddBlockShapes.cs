using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBlockShapes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "block_shape_queue",
                columns: table => new
                {
                    block_id = table.Column<Guid>(type: "uuid", nullable: false),
                    queued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "clock_timestamp()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_block_shape_queue", x => x.block_id);
                });

            migrationBuilder.CreateTable(
                name: "block_shapes",
                columns: table => new
                {
                    block_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shape = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    preview = table.Column<string>(type: "text", nullable: true),
                    words = table.Column<int>(type: "integer", nullable: false),
                    characters = table.Column<int>(type: "integer", nullable: false),
                    validation_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    validation_kind = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    warnings = table.Column<int>(type: "integer", nullable: false),
                    computed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_block_shapes", x => x.block_id);
                    table.ForeignKey(
                        name: "FK_block_shapes_blocks_block_id",
                        column: x => x.block_id,
                        principalTable: "blocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_block_shapes_document",
                table: "block_shapes",
                column: "document_id");

            // Every write to a block (EF, ExecuteUpdate or raw SQL) or to its validation queues it;
            // the API's worker (BlockShapeProcessor) refills its shape. A block queued again while
            // waiting gets the newer time, so a write during a batch is never lost.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION queue_block_shape_from_block() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    INSERT INTO block_shape_queue (block_id, queued_at) VALUES (NEW.id, clock_timestamp())
                    ON CONFLICT (block_id) DO UPDATE SET queued_at = EXCLUDED.queued_at;
                    RETURN NULL;
                END $$;
                CREATE OR REPLACE FUNCTION queue_block_shape_from_validation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    INSERT INTO block_shape_queue (block_id, queued_at) VALUES (NEW.block_id, clock_timestamp())
                    ON CONFLICT (block_id) DO UPDATE SET queued_at = EXCLUDED.queued_at;
                    RETURN NULL;
                END $$;
                CREATE TRIGGER trg_blocks_queue_shape AFTER INSERT OR UPDATE OF content, type ON blocks
                    FOR EACH ROW EXECUTE FUNCTION queue_block_shape_from_block();
                CREATE TRIGGER trg_block_validations_queue_shape AFTER INSERT OR UPDATE ON block_validations
                    FOR EACH ROW EXECUTE FUNCTION queue_block_shape_from_validation();
                -- Every existing block gets its shape on the worker's first passes.
                INSERT INTO block_shape_queue (block_id, queued_at) SELECT id, clock_timestamp() FROM blocks
                ON CONFLICT (block_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_blocks_queue_shape ON blocks;
                DROP TRIGGER IF EXISTS trg_block_validations_queue_shape ON block_validations;
                DROP FUNCTION IF EXISTS queue_block_shape_from_block();
                DROP FUNCTION IF EXISTS queue_block_shape_from_validation();
                """);
            migrationBuilder.DropTable(
                name: "block_shape_queue");

            migrationBuilder.DropTable(
                name: "block_shapes");
        }
    }
}
