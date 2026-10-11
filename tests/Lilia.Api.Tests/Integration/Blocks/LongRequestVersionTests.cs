using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Integration.Blocks;

/// <summary>
/// Ask Lilia edits through several tool calls in one request (one DbContext).
/// When another writer moved the document's Version meanwhile (an auto-version
/// snapshot), every later block save failed the concurrency check (11 Oct, on a
/// phone: "add_block keeps failing with a concurrency error", nothing changed).
/// </summary>
public class LongRequestVersionTests : IntegrationTestBase
{
    public LongRequestVersionTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Blocks_still_save_after_another_writer_moved_the_version()
    {
        await SeedUserAsync("lr-owner", "lr-owner@lilia.test", "Owner");
        var doc = await SeedDocumentAsync("lr-owner", "Long request");

        using var scope = Fixture.Factory.Services.CreateScope();
        var blocks = scope.ServiceProvider.GetRequiredService<IBlockService>();
        var text = (string t) => JsonDocument.Parse($"{{\"text\":\"{t}\"}}").RootElement;

        // The request's first change: the document is now tracked in its context.
        await blocks.CreateBlockAsync(doc.Id, new CreateBlockDto("paragraph", text("first"), null, null, null));

        // Someone else moves the version (as the auto-version handler does).
        await using (var other = CreateDbContext())
        {
            var d = await other.Documents.SingleAsync(x => x.Id == doc.Id);
            d.Version += 1;
            await other.SaveChangesAsync();
        }

        // The same request goes on: this used to throw DbUpdateConcurrencyException.
        var second = await blocks.CreateBlockAsync(doc.Id, new CreateBlockDto("paragraph", text("second"), null, null, null));
        var edited = await blocks.UpdateBlockAsync(doc.Id, second.Id, new UpdateBlockDto(null, text("second, edited"), null, null, null));
        edited.Should().NotBeNull();

        await using var check = CreateDbContext();
        (await check.Blocks.CountAsync(b => b.DocumentId == doc.Id)).Should().Be(2);
    }
}
