using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Integration.Documents;

/// <summary>
/// block_shapes stays current as the document lives, whichever path writes the block: the
/// triggers queue it, the processor shapes it, deleting the block deletes its shape.
/// </summary>
[Collection("Integration")]
public class BlockShapesTests : IntegrationTestBase
{
    public BlockShapesTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task ProcessAllAsync()
    {
        await using var db = CreateDbContext();
        while (await BlockShapeProcessor.ProcessAsync(db, 500) > 0) { }
    }

    private async Task<BlockShape?> ShapeAsync(Guid blockId)
    {
        await using var db = CreateDbContext();
        return await db.BlockShapes.AsNoTracking().FirstOrDefaultAsync(s => s.BlockId == blockId);
    }

    [Fact]
    public async Task A_new_block_gets_its_shape_without_its_text()
    {
        await SeedUserAsync("shape_new");
        var doc = await SeedDocumentAsync("shape_new");
        var block = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"My confidential sentence of seven words"}""");
        await ProcessAllAsync();

        var shape = await ShapeAsync(block.Id);
        shape.Should().NotBeNull();
        shape!.DocumentId.Should().Be(doc.Id);
        shape.Shape.Should().Be("Paragraph · 6 words");
        shape.Words.Should().Be(6);
        (shape.Shape + shape.Preview).Should().NotContainEquivalentOf("confidential");
    }

    [Fact]
    public async Task A_change_through_raw_SQL_is_shaped_again()
    {
        await SeedUserAsync("shape_sql");
        var doc = await SeedDocumentAsync("shape_sql");
        var block = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"two words"}""");
        await ProcessAllAsync();
        (await ShapeAsync(block.Id))!.Words.Should().Be(2);

        // Like the type-conversion and merge paths: an UPDATE that bypasses EF's change tracking.
        await using (var db = CreateDbContext())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE blocks SET content = '{{\"text\":\"now five words in it\"}}'::jsonb WHERE id = {block.Id}");
        await ProcessAllAsync();
        (await ShapeAsync(block.Id))!.Words.Should().Be(5);
    }

    [Fact]
    public async Task A_failed_validation_shows_as_a_kind_and_the_message_stays_out()
    {
        await SeedUserAsync("shape_val");
        var doc = await SeedDocumentAsync("shape_val");
        var block = await SeedBlockAsync(doc.Id, "equation", """{"source":"\\badmacro x"}""");
        await using (var db = CreateDbContext())
        {
            db.BlockValidations.Add(new BlockValidation
            {
                Id = Guid.NewGuid(), BlockId = block.Id, DocumentId = doc.Id, ContentHash = "h", Status = "error",
                ErrorMessage = "Undefined control sequence.\nl.1 my secret formula \\badmacro x", RuleVersion = "v1",
                Validator = "pdflatex", ValidatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        await ProcessAllAsync();

        var shape = await ShapeAsync(block.Id);
        shape!.ValidationStatus.Should().Be("error");
        shape.ValidationKind.Should().Be("Undefined control sequence \\badmacro");
        shape.Shape.Should().StartWith("Formula");
        (shape.Shape + shape.Preview + shape.ValidationKind).Should().NotContain("secret");
    }

    [Fact]
    public async Task Deleting_a_block_deletes_its_shape_and_a_deleted_blocks_queue_entry_is_dropped()
    {
        await SeedUserAsync("shape_del");
        var doc = await SeedDocumentAsync("shape_del");
        var kept = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"stays"}""");
        var gone = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"goes"}""", 1);
        await ProcessAllAsync();
        (await ShapeAsync(gone.Id)).Should().NotBeNull();

        await using (var db = CreateDbContext())
            await db.Blocks.Where(b => b.Id == gone.Id).ExecuteDeleteAsync();
        (await ShapeAsync(gone.Id)).Should().BeNull("the shape is deleted with its block");
        (await ShapeAsync(kept.Id)).Should().NotBeNull();

        await using var check = CreateDbContext();
        (await check.BlockShapeQueue.AnyAsync(q => q.BlockId == gone.Id)).Should().BeFalse();
    }
}
