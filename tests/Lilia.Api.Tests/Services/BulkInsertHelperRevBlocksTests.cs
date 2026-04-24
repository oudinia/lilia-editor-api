using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Round-trip + volume coverage for
/// <see cref="BulkInsertHelper.BulkInsertRevBlocksAsync"/>. Used by the
/// FT-IMP-001 parser dual-write (handoff §5 #1). These tests pin the
/// COPY-protocol column order and type mapping so wiring into
/// LatexImportJobExecutor later is a one-line change, not a debug
/// session.
/// </summary>
[Collection("Integration")]
public class BulkInsertHelperRevBlocksTests : IntegrationTestBase
{
    public BulkInsertHelperRevBlocksTests(TestDatabaseFixture fixture) : base(fixture) { }

    private new LiliaDbContext CreateDbContext() =>
        Fixture.Factory.Services.CreateScope()
            .ServiceProvider.GetRequiredService<LiliaDbContext>();

    private BulkInsertHelper CreateHelper() =>
        new(CreateDbContext());

    /// <summary>
    /// Seed ImportReviewSession + RevDocument so rev_blocks.rev_document_id
    /// has a parent. Returns the rev-document id the test should fan out
    /// rev_block rows into.
    /// </summary>
    private async Task<Guid> SeedRevDocumentAsync(string userId)
    {
        await SeedUserAsync(userId);
        await using var db = CreateDbContext();

        var sessionId = Guid.NewGuid();
        db.ImportReviewSessions.Add(new ImportReviewSession
        {
            Id = sessionId,
            OwnerId = userId,
            DocumentTitle = "bulk-rev.tex",
            SourceFormat = "tex",
            Status = "pending_review",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        });

        var revDocId = Guid.NewGuid();
        db.RevDocuments.Add(new RevDocument
        {
            Id = revDocId,
            InstanceId = sessionId,
            Title = "Bulk",
            SourceFormat = "tex",
        });
        await db.SaveChangesAsync();
        return revDocId;
    }

    [Fact]
    public async Task BulkInsertRevBlocksAsync_RoundTripsEveryColumn()
    {
        var revDocId = await SeedRevDocumentAsync("test_user_bulk_rev_roundtrip");
        var helper = CreateHelper();

        var rows = new List<RevBlock>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RevDocumentId = revDocId,
                Type = "heading",
                Content = JsonDocument.Parse("{\"text\":\"Title\",\"level\":1}"),
                SortOrder = 0,
                Depth = 0,
                Status = "kept",
                Metadata = JsonDocument.Parse("{\"imported\":true}"),
                Confidence = 95,
                Warnings = JsonDocument.Parse("[{\"code\":\"auto-toc\"}]"),
                Path = "/root/0",
            },
            new()
            {
                Id = Guid.NewGuid(),
                RevDocumentId = revDocId,
                Type = "paragraph",
                Content = JsonDocument.Parse("{\"text\":\"Body text.\"}"),
                SortOrder = 100,
                Depth = 0,
                Status = "kept",
                Metadata = JsonDocument.Parse("{}"),
                // Confidence deliberately null → exercises WriteNullableIntAsync
                Confidence = null,
                Warnings = null,
                Path = null,
            },
        };

        var inserted = await helper.BulkInsertRevBlocksAsync(rows);
        inserted.Should().Be(2);

        await using var db = CreateDbContext();
        var loaded = await db.RevBlocks
            .Where(b => b.RevDocumentId == revDocId)
            .OrderBy(b => b.SortOrder)
            .ToListAsync();

        loaded.Should().HaveCount(2);

        // First row — every column populated.
        loaded[0].Id.Should().Be(rows[0].Id);
        loaded[0].Type.Should().Be("heading");
        loaded[0].Content.RootElement.GetProperty("text").GetString().Should().Be("Title");
        loaded[0].Content.RootElement.GetProperty("level").GetInt32().Should().Be(1);
        loaded[0].SortOrder.Should().Be(0);
        loaded[0].Status.Should().Be("kept");
        loaded[0].Confidence.Should().Be(95);
        loaded[0].Path.Should().Be("/root/0");
        loaded[0].Warnings.Should().NotBeNull();
        loaded[0].Metadata.RootElement.GetProperty("imported").GetBoolean().Should().BeTrue();

        // Second row — nullable columns actually stored NULL.
        loaded[1].Confidence.Should().BeNull();
        loaded[1].Path.Should().BeNull();
        loaded[1].Warnings.Should().BeNull();
    }

    [Fact]
    public async Task BulkInsertRevBlocksAsync_HandlesEmptyInput()
    {
        // The parser is allowed to hand over an empty enumerable when
        // the source document has no block-level content. COPY must
        // handle zero rows without opening a transaction to nothing.
        _ = await SeedRevDocumentAsync("test_user_bulk_rev_empty");
        var helper = CreateHelper();

        var inserted = await helper.BulkInsertRevBlocksAsync(Enumerable.Empty<RevBlock>());
        inserted.Should().Be(0);
    }

    [Fact]
    public async Task BulkInsertRevBlocksAsync_LargeBatch_InsertsAllRows()
    {
        // 500 rows — slightly beyond the "realistic big import" size
        // a 100-page PDF would generate. Smoke test for the COPY
        // protocol's buffering under load.
        var revDocId = await SeedRevDocumentAsync("test_user_bulk_rev_large");
        var helper = CreateHelper();

        const int count = 500;
        var rows = Enumerable.Range(0, count).Select(i => new RevBlock
        {
            Id = Guid.NewGuid(),
            RevDocumentId = revDocId,
            Type = "paragraph",
            Content = JsonDocument.Parse($"{{\"text\":\"para-{i}\"}}"),
            SortOrder = i * 100,
            Depth = 0,
            Status = "kept",
            Metadata = JsonDocument.Parse("{}"),
        });

        var inserted = await helper.BulkInsertRevBlocksAsync(rows);
        inserted.Should().Be(count);

        await using var db = CreateDbContext();
        var dbCount = await db.RevBlocks.CountAsync(b => b.RevDocumentId == revDocId);
        dbCount.Should().Be(count);
    }
}
