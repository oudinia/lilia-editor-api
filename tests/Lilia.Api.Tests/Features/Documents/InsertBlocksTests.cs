using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Features.Documents;

/// <summary>
/// launch-bot #148 (18 Nov 2026): a paste rewrote every block's sort order, so each paste on a 600-block document
/// updated 600 rows. <see cref="IBlockService.InsertBlocksAsync"/> writes the new blocks and moves only what must move.
/// </summary>
public class InsertBlocksTests : IntegrationTestBase
{
    public InsertBlocksTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static BatchUpdateBlockDto New(string text) =>
        new(Guid.NewGuid(), "paragraph", JsonDocument.Parse($"{{\"text\":\"{text}\"}}").RootElement, null, null, 0);

    private async Task<(Guid Doc, List<Guid> Ids)> SeedAsync(string user, params int[] sorts)
    {
        await SeedUserAsync(user);
        var doc = await SeedDocumentAsync(user, "Insert");
        var ids = new List<Guid>();
        foreach (var s in sorts) ids.Add((await SeedBlockAsync(doc.Id, "paragraph", $"{{\"text\":\"b{s}\"}}", s)).Id);
        await using var db = CreateDbContext();
        // A known old UpdatedAt, to see which rows were written.
        await db.Blocks.Where(b => b.DocumentId == doc.Id).ExecuteUpdateAsync(u => u.SetProperty(b => b.UpdatedAt, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        return (doc.Id, ids);
    }

    private async Task<List<(Guid Id, int Sort, bool Written)>> RowsAsync(Guid doc)
    {
        await using var db = CreateDbContext();
        return (await db.Blocks.AsNoTracking().Where(b => b.DocumentId == doc).OrderBy(b => b.SortOrder).ToListAsync())
            .Select(b => (b.Id, b.SortOrder, b.UpdatedAt.Year > 2020)).ToList();
    }

    private async Task<BatchUpdateResultDto> InsertAsync(Guid doc, Guid? after, params BatchUpdateBlockDto[] blocks)
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        int version;
        await using (var db = CreateDbContext()) version = (await db.Documents.SingleAsync(d => d.Id == doc)).Version;
        return await scope.ServiceProvider.GetRequiredService<IBlockService>().InsertBlocksAsync(doc, after, blocks.ToList(), version);
    }

    [Fact]
    public async Task At_the_end_no_existing_block_is_written()
    {
        var (doc, ids) = await SeedAsync("ins_end", 0, 1, 2);
        var a = New("a");
        var b = New("b");

        var result = await InsertAsync(doc, null, a, b);

        var rows = await RowsAsync(doc);
        rows.Select(r => r.Id).Should().Equal(ids[0], ids[1], ids[2], a.Id, b.Id);
        rows.Take(3).Should().OnlyContain(r => !r.Written);
        rows.Select(r => r.Sort).Should().Equal(0, 1, 2, 3, 4);
        result.Blocks.Select(x => x.Id).Should().Equal(rows.Select(r => r.Id));
    }

    [Fact]
    public async Task In_the_middle_only_the_blocks_after_it_move()
    {
        var (doc, ids) = await SeedAsync("ins_mid", 0, 1, 2, 3);
        var a = New("a");
        var b = New("b");

        await InsertAsync(doc, ids[1], a, b);

        var rows = await RowsAsync(doc);
        rows.Select(r => r.Id).Should().Equal(ids[0], ids[1], a.Id, b.Id, ids[2], ids[3]);
        rows[0].Written.Should().BeFalse();
        rows[1].Written.Should().BeFalse();
        rows.Select(r => r.Sort).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task A_gap_big_enough_takes_the_blocks_without_moving_any()
    {
        var (doc, ids) = await SeedAsync("ins_gap", 0, 10, 20);
        var a = New("a");

        await InsertAsync(doc, ids[0], a);

        var rows = await RowsAsync(doc);
        rows.Select(r => r.Id).Should().Equal(ids[0], a.Id, ids[1], ids[2]);
        rows.Where(r => r.Id != a.Id).Should().OnlyContain(r => !r.Written);
    }

    [Fact]
    public async Task Tied_sort_orders_are_renumbered_as_before()
    {
        var (doc, ids) = await SeedAsync("ins_tie", 0, 1, 1, 2);
        var a = New("a");

        var result = await InsertAsync(doc, ids[3], a);

        var rows = await RowsAsync(doc);
        rows.Select(r => r.Sort).Should().Equal(0, 1, 2, 3, 4);
        rows.Last().Id.Should().Be(a.Id);
        result.Version.Should().BeGreaterThan(0);
    }
}
