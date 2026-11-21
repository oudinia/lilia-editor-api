using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Features.BulkImport.Contracts;
using Lilia.Api.Features.BulkImport.Services;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Features.BulkImport;

/// <summary>
/// Staging (21 Nov 2026, the owner: "a staging table for the whole import until it ends… so we don't clash with the final
/// tables"): the read-only preview and its figures (author only), the promotion into one document (idempotent, figures
/// moved to the document's assets), and a file split before staging still finishing in its document. Mathpix is the fake.
/// </summary>
public partial class BulkImportFlowTests
{
    /// <summary>
    /// Makes a file just split look as one split before staging did: its document made at once, one placeholder per
    /// chunk, <c>uses_staging</c> false (as every file split before the deploy). Returns the document.
    /// </summary>
    private async Task<Guid> AsSplitBeforeStagingAsync(Guid fileId)
    {
        await using var db = CreateDbContext();
        var file = await db.BulkImportFiles.SingleAsync(f => f.Id == fileId);
        var chunks = await db.BulkImportChunks.Where(c => c.FileId == fileId).OrderBy(c => c.Index).ToListAsync();
        var documentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO documents (id, owner_id, title, created_at, updated_at) VALUES ({documentId}, {file.UserId}, {"Old flow"}, {now}, {now})");
        foreach (var c in chunks)
        {
            c.PlaceholderBlockId = Guid.NewGuid();
            db.Blocks.Add(new Block
            {
                Id = c.PlaceholderBlockId.Value, DocumentId = documentId, Type = "paragraph",
                Content = JsonDocument.Parse(JsonSerializer.Serialize(new { text = BulkImportProcessor.ReadingText(c) })),
                SortOrder = c.Index, CreatedAt = now, UpdatedAt = now,
            });
        }
        file.UsesStaging = false;
        file.DocumentId = documentId;
        await db.SaveChangesAsync();
        return documentId;
    }

    private async Task<Dictionary<int, Guid>> ChunkIdsAsync(Guid fileId)
    {
        await using var db = CreateDbContext();
        return await db.BulkImportChunks.AsNoTracking().Where(c => c.FileId == fileId).ToDictionaryAsync(c => c.Index, c => c.Id);
    }

    // ── an old-flow file finishes in its document ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_file_split_before_staging_still_finishes_in_its_document()
    {
        var host = Host(maxChunksPerAuthor: 10);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        var uploaded = await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(25, salt: Guid.NewGuid().ToString()));
        await SplitAsync(host);
        var documentId = await AsSplitBeforeStagingAsync(uploaded.FileId);

        await PumpAsync(host);

        var file = (await BatchAsync(author, batchId)).Files.Single();
        file.Status.Should().Be("done");
        file.DocumentId.Should().Be(documentId, "its document from the split, never a second one");
        file.PromotedAt.Should().BeNull("an old-flow file is never promoted");
        (await DocumentsOfAsync(userId)).Should().Equal(documentId);
        var texts = (await BlocksAsync(documentId)).Select(b => b.Text).ToList();
        texts.Should().NotContain(t => t.Contains("being read"));
        new[] { "pages 1 to 10", "pages 11 to 20", "pages 21 to 25" }
            .Select(t => texts.FindIndex(x => x.Contains(t))).Should().OnlyContain(i => i >= 0).And.BeInAscendingOrder();
        await using var db = CreateDbContext();
        (await db.BulkImportStagedBlocks.AsNoTracking().CountAsync(b => b.FileId == file.FileId)).Should().Be(0, "nothing was staged");
        (await db.BulkImportFiles.AsNoTracking().SingleAsync(f => f.Id == file.FileId)).PromoteMs.Should().BeNull();
        _compiler.Compiled.Should().Contain(documentId);
    }

    // ── promotion is idempotent ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Promotion_makes_one_document_however_often_and_at_once_it_is_delivered()
    {
        var host = Host(maxChunksPerAuthor: 10);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        var uploaded = await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(25, salt: Guid.NewGuid().ToString()));

        // Every step but the promotion.
        var held = new List<PromoteBulkImportFile>();
        for (var i = 0; i < 500 && _queue.Sent.TryDequeue(out var item); i++)
        {
            if (item.Message is PromoteBulkImportFile p) held.Add(p);
            else await RunAsync(host, item.Message);
        }
        held.Should().NotBeEmpty();
        (await DocumentsOfAsync(userId)).Should().BeEmpty();

        // Delivered three times, two of them at the same moment.
        await Task.WhenAll(RunAsync(host, held[0]), RunAsync(host, new PromoteBulkImportFile(uploaded.FileId)));
        await RunAsync(host, new PromoteBulkImportFile(uploaded.FileId));
        await PumpAsync(host);

        var documents = await DocumentsOfAsync(userId);
        documents.Should().ContainSingle("a redelivered promotion never makes a second document");
        var file = (await BatchAsync(author, batchId)).Files.Single();
        file.Status.Should().Be("done");
        file.DocumentId.Should().Be(documents[0]);
        var texts = (await BlocksAsync(documents[0])).Select(b => b.Text).ToList();
        texts.Count(t => t.Contains("pages 1 to 10")).Should().Be(1, "each block once");
        _compiler.Compiled.Should().ContainSingle();
    }

    // ── the preview ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_preview_gives_staged_blocks_and_placeholders_in_page_order_to_the_author_only()
    {
        var host = Host(maxChunksPerAuthor: 1);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        var uploaded = await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(35, salt: Guid.NewGuid().ToString()));
        await SplitAsync(host);
        var ids = await ChunkIdsAsync(uploaded.FileId);
        _queue.Sent.Clear();

        // Pages 1–10 fail (three attempts), 11–20 are read and staged, 21–30 are being read, 31–35 wait.
        _mathpix.FailFirstPages.Add(1);
        for (var attempt = 0; attempt < 3; attempt++) await RunAsync(host, new ReadBulkImportChunk(ids[0]));
        await RunAsync(host, new ReadBulkImportChunk(ids[1]));
        await RunAsync(host, new PlaceBulkImportChunk(ids[1]));
        _queue.Sent.Clear();

        var res = await author.GetAsync($"/api/bulk-imports/{batchId}/files/{uploaded.FileId}/preview");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var preview = (await res.Content.ReadFromJsonAsync<BulkImportPreviewDto>())!;
        preview.FileId.Should().Be(uploaded.FileId);
        preview.Name.Should().Be("Forkosh notebook 3.pdf");
        preview.Pages.Should().Be(35);
        preview.PagesDone.Should().Be(10);
        preview.Status.Should().Be("reading");
        preview.DocumentId.Should().BeNull();

        var blocks = preview.Blocks;
        blocks.First().Should().Match<BulkImportPreviewBlockDto>(b => b.Type == "placeholder" && b.ChunkIndex == 0 && b.Id == null);
        Placeholder(blocks.First()).Should().Be((1, 10, "failed"));
        var stagedBlocks = blocks.Where(b => b.ChunkIndex == 1).ToList();
        stagedBlocks.Should().NotBeEmpty().And.OnlyContain(b => b.Type != "placeholder" && b.Id != null);
        stagedBlocks.Select(b => b.Content.GetRawText()).Should().Contain(t => t.Contains("pages 11 to 20"));
        blocks.Select(b => b.ChunkIndex).Should().BeInAscendingOrder();
        Placeholder(blocks[^2]).Should().Be((21, 30, "reading"));
        Placeholder(blocks[^1]).Should().Be((31, 35, "queued"));

        // The raw JSON keeps the contract's names.
        var raw = await res.Content.ReadAsStringAsync();
        raw.Should().Contain("\"fileId\"").And.Contain("\"pagesDone\"").And.Contain("\"chunkIndex\"").And.Contain("\"firstPage\"").And.Contain("\"state\"");

        // Told: the failed chunk and the staged one.
        _notifier.PreviewChanges.Should().Contain((batchId, uploaded.FileId, 0)).And.Contain((batchId, uploaded.FileId, 1));
        var dto = (await BatchAsync(author, batchId)).Files.Single();
        dto.PreviewAvailable.Should().BeTrue();

        // Nobody else: 404, as for a batch.
        var other = ClientOf(host, NewId("p"));
        (await other.GetAsync($"/api/bulk-imports/{batchId}/files/{uploaded.FileId}/preview")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await author.GetAsync($"/api/bulk-imports/{Guid.NewGuid()}/files/{uploaded.FileId}/preview")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CreateAnonymousClient().GetAsync($"/api/bulk-imports/{batchId}/files/{uploaded.FileId}/preview")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static (int First, int Last, string State) Placeholder(BulkImportPreviewBlockDto block)
    {
        block.Type.Should().Be("placeholder");
        return (block.Content.GetProperty("firstPage").GetInt32(), block.Content.GetProperty("lastPage").GetInt32(),
            block.Content.GetProperty("state").GetString()!);
    }

    // ── staged figures: served to the author, moved to the document's assets at promotion ─────────────────────

    [Fact]
    public async Task A_staged_figure_is_the_authors_only_and_becomes_a_document_asset_at_promotion()
    {
        var host = Host(maxChunksPerAuthor: 1);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        _mathpix.FigureFirstPages.Add(1);
        var uploaded = await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(15, salt: Guid.NewGuid().ToString()));
        await SplitAsync(host);
        var ids = await ChunkIdsAsync(uploaded.FileId);
        _queue.Sent.Clear();
        await RunAsync(host, new ReadBulkImportChunk(ids[0]));
        await RunAsync(host, new PlaceBulkImportChunk(ids[0]));

        var preview = (await author.GetFromJsonAsync<BulkImportPreviewDto>($"/api/bulk-imports/{batchId}/files/{uploaded.FileId}/preview"))!;
        var figure = preview.Blocks.Single(b => b.Type == "figure");
        var src = figure.Content.GetProperty("src").GetString()!;
        var prefix = $"/api/bulk-imports/{batchId}/files/{uploaded.FileId}/assets/";
        src.Should().StartWith(prefix);

        var image = await author.GetAsync(src);
        image.StatusCode.Should().Be(HttpStatusCode.OK);
        image.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        image.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        (await image.Content.ReadAsByteArrayAsync()).Should().Equal(FakeMathpixClient.Png);

        // Anyone else, or a name that isn't staged: 404.
        (await ClientOf(host, NewId("i")).GetAsync(src)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await author.GetAsync(prefix + "..%2F..%2Fsource.pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await author.GetAsync(prefix + $"{Guid.NewGuid():N}.png")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Only raster images: a staged SVG is never served.
        var svgName = $"{Guid.NewGuid():N}.svg";
        await using (var db = CreateDbContext())
        {
            db.BulkImportStagedAssets.Add(new BulkImportStagedAsset
            {
                Id = Guid.NewGuid(), FileId = uploaded.FileId, ChunkIndex = 0, Name = svgName, OriginalPath = "images/x.svg",
                StorageKey = $"bulk-staging/{uploaded.FileId:N}/{svgName}", ContentType = "image/svg+xml", Size = 10,
            });
            await db.SaveChangesAsync();
        }
        (await author.GetAsync(prefix + svgName)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using (var db = CreateDbContext())
            await db.BulkImportStagedAssets.Where(a => a.Name == svgName).ExecuteDeleteAsync();

        // The rest, then the promotion: the figure points at the document's own asset, the staged copy is gone.
        string stagedKey;
        await using (var db = CreateDbContext())
            stagedKey = await db.BulkImportStagedAssets.AsNoTracking().Where(a => a.FileId == uploaded.FileId).Select(a => a.StorageKey).SingleAsync();
        await PumpAsync(host);

        var file = (await BatchAsync(author, batchId)).Files.Single();
        file.Status.Should().Be("done");
        await using (var db = CreateDbContext())
        {
            var block = await db.Blocks.AsNoTracking().SingleAsync(b => b.DocumentId == file.DocumentId && b.Type == "figure");
            var content = block.Content.RootElement;
            content.TryGetProperty("stagedAssetId", out _).Should().BeFalse();
            var assetId = Guid.Parse(content.GetProperty("assetId").GetString()!);
            var asset = await db.Assets.AsNoTracking().SingleAsync(a => a.Id == assetId);
            asset.DocumentId.Should().Be(file.DocumentId!.Value);
            asset.StorageKey.Should().StartWith($"{userId}/documents/{file.DocumentId}/import-assets/");
            content.GetProperty("src").GetString().Should().Be(asset.Url);
            var storage = host.Services.GetRequiredService<Lilia.Core.Interfaces.IStorageService>();
            await using (var moved = await storage.DownloadAsync(asset.StorageKey))
            using (var buffer = new MemoryStream())
            {
                await moved.CopyToAsync(buffer);
                buffer.ToArray().Should().Equal(FakeMathpixClient.Png);
            }
            var gone = async () => (await storage.DownloadAsync(stagedKey)).Dispose();
            await gone.Should().ThrowAsync<Exception>();
        }
        (await author.GetAsync(src)).StatusCode.Should().Be(HttpStatusCode.NotFound, "promoted: the document's asset has it");
    }

    // ── one file cancelled ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancelling_one_file_before_promotion_deletes_its_staging_and_a_retry_stages_it_again()
    {
        var host = Host(maxChunksPerAuthor: 10);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        var uploaded = await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(15, salt: Guid.NewGuid().ToString()));
        await SplitAsync(host);
        var ids = await ChunkIdsAsync(uploaded.FileId);
        _queue.Sent.Clear();
        await RunAsync(host, new ReadBulkImportChunk(ids[0]));
        await RunAsync(host, new PlaceBulkImportChunk(ids[0]));
        _queue.Sent.Clear();

        (await ClientOf(host, NewId("c")).PostAsync($"/api/bulk-imports/{batchId}/files/{uploaded.FileId}/cancel", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        var cancel = await author.PostAsync($"/api/bulk-imports/{batchId}/files/{uploaded.FileId}/cancel", null);
        cancel.StatusCode.Should().Be(HttpStatusCode.Accepted, await cancel.Content.ReadAsStringAsync());
        await PumpAsync(host);
        var file = (await BatchAsync(author, batchId)).Files.Single();
        file.Status.Should().Be("cancelled");
        file.DocumentId.Should().BeNull();
        await using (var db = CreateDbContext())
            (await db.BulkImportStagedBlocks.AsNoTracking().CountAsync(b => b.FileId == file.FileId)).Should().Be(0);
        (await author.PostAsync($"/api/bulk-imports/{batchId}/files/{uploaded.FileId}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Retry: the chunk already read is staged again from its stored reading (no Mathpix), the other is read.
        var submits = _mathpix.Submits;
        (await author.PostAsync($"/api/bulk-imports/{batchId}/files/{file.FileId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await PumpAsync(host);
        var done = (await BatchAsync(author, batchId)).Files.Single();
        done.Status.Should().Be("done");
        _mathpix.Submits.Should().Be(submits + 1);
        var texts = (await BlocksAsync(done.DocumentId!.Value)).Select(b => b.Text).ToList();
        new[] { "pages 1 to 10", "pages 11 to 15" }.Select(t => texts.FindIndex(x => x.Contains(t)))
            .Should().OnlyContain(i => i >= 0).And.BeInAscendingOrder();
    }
}
