using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Engines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Lilia.Api.Tests.Integration.Sync;

/// <summary>
/// Every write to a document's blocks advances its version — so the Flow
/// editor, which saves the whole document at once, gets a 409 and rebases
/// instead of silently deleting or overwriting what was written elsewhere.
/// Real Postgres.
/// </summary>
[Collection("Integration")]
public class SingleBlockWritesAdvanceVersionTests : IntegrationTestBase
{
    public SingleBlockWritesAdvanceVersionTests(TestDatabaseFixture fixture) : base(fixture) { }

    private BlockService Blocks() => new(
        CreateDbContext(),
        NullLogger<BlockService>.Instance,
        new Mock<IPreviewCacheService>().Object,
        new Mock<IBlockTypeService>().Object);

    private StudioService Studio() => new(
        CreateDbContext(), new Mock<IRenderService>().Object, NullLogger<StudioService>.Instance);

    private async Task<Guid> ADocument()
    {
        var owner = $"user-{Guid.NewGuid():N}";
        await SeedUserAsync(owner);
        return (await SeedDocumentAsync(owner)).Id;
    }

    private async Task<int> VersionOf(Guid id)
    {
        await using var db = CreateDbContext();
        return (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == id)).Version;
    }

    private static JsonElement Text(string t) => JsonSerializer.SerializeToElement(new { text = t });

    [Fact]
    public async Task Create_update_delete_and_reorder_each_advance_the_version()
    {
        var id = await ADocument();
        var v0 = await VersionOf(id);

        var a = await Blocks().CreateBlockAsync(id, new CreateBlockDto("paragraph", Text("a"), 0, null, 0));
        (await VersionOf(id)).Should().Be(v0 + 1, "create");

        await Blocks().UpdateBlockAsync(id, a.Id, new UpdateBlockDto(null, Text("a2"), null, null, null));
        (await VersionOf(id)).Should().Be(v0 + 2, "update");

        await Blocks().ReorderBlocksAsync(id, [a.Id]);
        (await VersionOf(id)).Should().Be(v0 + 3, "reorder");

        await Blocks().DeleteBlockAsync(id, a.Id);
        (await VersionOf(id)).Should().Be(v0 + 4, "delete");
    }

    [Fact]
    public async Task A_conversion_advances_the_version()
    {
        var id = await ADocument();
        var a = await Blocks().CreateBlockAsync(id, new CreateBlockDto("paragraph", Text("Intro"), 0, null, 0));
        var before = await VersionOf(id);

        await Blocks().ConvertBlockAsync(id, a.Id, "heading");

        (await VersionOf(id)).Should().Be(before + 1);
    }

    [Fact]
    public async Task Studio_writes_advance_the_version()
    {
        var id = await ADocument();
        var v0 = await VersionOf(id);

        var node = await Studio().CreateBlockAsync(id, new CreateBlockDto("paragraph", Text("s"), null, null, 0));
        (await VersionOf(id)).Should().Be(v0 + 1, "create");

        await Studio().UpdateBlockContentAsync(id, node.Id, new UpdateBlockDto(null, Text("s2"), null, null, null));
        (await VersionOf(id)).Should().Be(v0 + 2, "update");

        await Studio().DeleteBlockAsync(id, node.Id);
        (await VersionOf(id)).Should().Be(v0 + 3, "delete");
    }

    [Fact]
    public async Task A_whole_document_save_on_the_old_version_is_refused_instead_of_deleting_the_new_block()
    {
        // The data loss this closes: the Flow editor holds version v and a
        // payload without Studio's new block. Before, v was still current, the
        // save went through, and the batch deleted the block it didn't mention.
        var id = await ADocument();
        var mine = await Blocks().CreateBlockAsync(id, new CreateBlockDto("paragraph", Text("mine"), 0, null, 0));
        var flowHolds = await VersionOf(id);

        var fromStudio = await Studio().CreateBlockAsync(id, new CreateBlockDto("paragraph", Text("from Studio"), 1, null, 0));

        var save = () => Blocks().BatchUpdateBlocksAsync(id,
            [new BatchUpdateBlockDto(mine.Id, "paragraph", Text("mine, edited"), 0, null, 0)],
            expectedVersion: flowHolds);

        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var db = CreateDbContext();
        (await db.Blocks.AnyAsync(b => b.Id == fromStudio.Id)).Should().BeTrue("the refused save deleted nothing");
    }
}
