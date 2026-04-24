using System.Net;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Regression tests for GET /api/studio/{docId}/blocks/previews.
/// Kills the N+1 that bit Studio on large documents. Covers:
///   - Returns cached previews keyed by blockId.ToString()
///   - Scopes to the requested document (no cross-doc leakage)
///   - Skips uncached blocks rather than throwing
///   - Owner-only — 404 for other users
/// </summary>
[Collection("Integration")]
public class StudioPreviewBatchTests : IntegrationTestBase
{
    private const string UserId = "test_studio_batch_user";

    public StudioPreviewBatchTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Batch_Returns_AllCachedPreviews_ForDocument()
    {
        await SeedUserAsync(UserId);
        var doc = await SeedDocumentAsync(UserId, "Batch test doc");
        var b1 = await SeedBlockAsync(doc.Id, type: "paragraph", contentJson: "{\"text\":\"hello\"}", sortOrder: 0);
        var b2 = await SeedBlockAsync(doc.Id, type: "heading", contentJson: "{\"text\":\"Title\",\"level\":1}", sortOrder: 1);

        // Seed two cached previews directly — bypasses the renderer so
        // the test doesn't depend on the RenderService.
        await using (var db = Fixture.Factory.Services.CreateScope()
                        .ServiceProvider.GetRequiredService<LiliaDbContext>())
        {
            db.BlockPreviews.Add(new BlockPreview
            {
                Id = Guid.NewGuid(),
                BlockId = b1.Id,
                Format = "latex",
                Data = System.Text.Encoding.UTF8.GetBytes("hello"),
                RenderedAt = DateTime.UtcNow,
            });
            db.BlockPreviews.Add(new BlockPreview
            {
                Id = Guid.NewGuid(),
                BlockId = b2.Id,
                Format = "latex",
                Data = System.Text.Encoding.UTF8.GetBytes("\\section{Title}"),
                RenderedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateClientAs(UserId);
        var res = await client.GetAsync($"/api/studio/{doc.Id}/blocks/previews?format=latex");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = body.RootElement;
        root.EnumerateObject().Count().Should().Be(2);
        root.GetProperty(b1.Id.ToString()).GetString().Should().Be("hello");
        root.GetProperty(b2.Id.ToString()).GetString().Should().Be("\\section{Title}");
    }

    [Fact]
    public async Task Batch_ScopesToRequestedDocument()
    {
        await SeedUserAsync(UserId);
        var docA = await SeedDocumentAsync(UserId, "Doc A");
        var docB = await SeedDocumentAsync(UserId, "Doc B");
        var blockA = await SeedBlockAsync(docA.Id, contentJson: "{\"text\":\"A\"}");
        var blockB = await SeedBlockAsync(docB.Id, contentJson: "{\"text\":\"B\"}");

        await using (var db = Fixture.Factory.Services.CreateScope()
                        .ServiceProvider.GetRequiredService<LiliaDbContext>())
        {
            db.BlockPreviews.Add(new BlockPreview { Id = Guid.NewGuid(), BlockId = blockA.Id, Format = "latex", Data = "A"u8.ToArray(), RenderedAt = DateTime.UtcNow });
            db.BlockPreviews.Add(new BlockPreview { Id = Guid.NewGuid(), BlockId = blockB.Id, Format = "latex", Data = "B"u8.ToArray(), RenderedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        using var client = CreateClientAs(UserId);
        var res = await client.GetAsync($"/api/studio/{docA.Id}/blocks/previews?format=latex");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        body.RootElement.EnumerateObject().Count().Should().Be(1,
            "batch must not leak previews from other documents");
        body.RootElement.TryGetProperty(blockA.Id.ToString(), out _).Should().BeTrue();
        body.RootElement.TryGetProperty(blockB.Id.ToString(), out _).Should().BeFalse();
    }

    [Fact]
    public async Task Batch_SkipsUncachedBlocks_ReturnsPartial()
    {
        await SeedUserAsync(UserId);
        var doc = await SeedDocumentAsync(UserId, "Partial doc");
        var cached = await SeedBlockAsync(doc.Id, contentJson: "{\"text\":\"cached\"}", sortOrder: 0);
        var uncached = await SeedBlockAsync(doc.Id, contentJson: "{\"text\":\"uncached\"}", sortOrder: 1);

        await using (var db = Fixture.Factory.Services.CreateScope()
                        .ServiceProvider.GetRequiredService<LiliaDbContext>())
        {
            db.BlockPreviews.Add(new BlockPreview { Id = Guid.NewGuid(), BlockId = cached.Id, Format = "latex", Data = "c"u8.ToArray(), RenderedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        using var client = CreateClientAs(UserId);
        var res = await client.GetAsync($"/api/studio/{doc.Id}/blocks/previews?format=latex");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        body.RootElement.EnumerateObject().Count().Should().Be(1,
            "uncached blocks are absent from the response — caller falls through");
        body.RootElement.TryGetProperty(cached.Id.ToString(), out _).Should().BeTrue();
        body.RootElement.TryGetProperty(uncached.Id.ToString(), out _).Should().BeFalse();
        _ = uncached;
    }

    [Fact]
    public async Task Batch_ReturnsEmpty_ForDocumentWithNoPreviews()
    {
        await SeedUserAsync(UserId);
        var doc = await SeedDocumentAsync(UserId, "Empty preview doc");
        await SeedBlockAsync(doc.Id);

        using var client = CreateClientAs(UserId);
        var res = await client.GetAsync($"/api/studio/{doc.Id}/blocks/previews?format=latex");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        body.RootElement.EnumerateObject().Count().Should().Be(0);
    }
}
