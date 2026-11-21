using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Features.BulkImport;
using Lilia.Api.Features.BulkImport.Contracts;
using Lilia.Api.Features.BulkImport.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Import.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lilia.Api.Tests.Features.BulkImport;

/// <summary>
/// Bulk import end to end (21 Nov 2026): the HTTP API, each chunk's reading staged and the document made in one go when the
/// file is finished (staging, the same day), per-page progress, the cache, failures, retry, cancelling, the plan, and
/// who may see a batch. Mathpix is <see cref="FakeMathpixClient"/> (no network); the queue is <see cref="RecordingQueue"/>,
/// so each test runs the split, read and place steps itself, in the order it chooses. One test runs the real Wolverine
/// queues (durable local, no broker) instead.
/// </summary>
public partial class BulkImportFlowTests : IntegrationTestBase, IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _factories = [];
    private readonly FakeMathpixClient _mathpix = new();
    private readonly RecordingQueue _queue = new();
    private readonly FakeBulkCompiler _compiler = new();
    private readonly RecordingNotifier _notifier = new();

    public BulkImportFlowTests(TestDatabaseFixture fixture) : base(fixture) { }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private static string NewId(string what) => $"bulk_{what}_{Guid.NewGuid():N}"[..30];

    private WebApplicationFactory<Program> Host(bool realQueues = false, bool enforce = false, int maxChunksPerAuthor = 2, string? staff = null)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Import:PdfSitePagesPerMonth", "1000000");
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMathpixClient>();
                services.AddSingleton<IMathpixClient>(_mathpix);
                // The compile after a file is done is timed with a fake: no TeX in these tests.
                services.RemoveAll<Lilia.Api.Services.IBulkImportCompiler>();
                services.AddSingleton<Lilia.Api.Services.IBulkImportCompiler>(_compiler);
                if (staff is not null)
                    services.PostConfigure<Lilia.Api.Services.EntitlementOptions>(o => o.StaffUserIds = [staff]);
                services.RemoveAll<IBulkImportNotifier>();
                services.AddSingleton<IBulkImportNotifier>(_notifier);
                services.RemoveAll<BulkImportOptions>();
                services.AddSingleton(new BulkImportOptions
                {
                    PagesPerChunk = 10,
                    MaxChunksPerAuthor = maxChunksPerAuthor,
                    PollSeconds = 0.01,
                    RetryDelaysSeconds = [0.01],
                    SweepEverySeconds = 0,
                });
                if (!realQueues)
                {
                    services.RemoveAll<IBulkImportQueue>();
                    services.AddSingleton<IBulkImportQueue>(_queue);
                }
                if (enforce)
                    services.PostConfigure<Lilia.Api.Services.EntitlementOptions>(o => { o.Enforce = true; o.StaffUserIds = []; });
            });
        });
        _factories.Add(factory);
        return factory;
    }

    private static HttpClient ClientOf(WebApplicationFactory<Program> factory, string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        client.DefaultRequestHeaders.Add("X-Test-Name", userId);
        return client;
    }

    private async Task<string> AuthorAsync(string plan = "pro")
    {
        var id = NewId("a");
        await SeedUserAsync(id);
        await using var db = CreateDbContext();
        var p = await db.Plans.SingleAsync(x => x.Slug == plan);
        db.UserPlans.Add(new UserPlan { Id = Guid.NewGuid(), UserId = id, PlanId = p.Id, Status = "active", StartedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> NewBatchAsync(HttpClient author)
    {
        var res = await author.PostAsync("/api/bulk-imports", null);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<BulkImportCreatedDto>())!.BatchId;
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient author, Guid batchId, byte[] pdf, string name = "Forkosh notebook 3.pdf")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", name);
        return author.PostAsync($"/api/bulk-imports/{batchId}/files", form);
    }

    private static async Task<BulkImportFileDto> UploadOkAsync(HttpClient author, Guid batchId, byte[] pdf)
    {
        var res = await UploadAsync(author, batchId, pdf);
        res.StatusCode.Should().Be(HttpStatusCode.Accepted, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<BulkImportFileDto>())!;
    }

    /// <summary>Runs one message through the processor, in its own scope as Wolverine would.</summary>
    private static async Task RunAsync(WebApplicationFactory<Program> host, object message)
    {
        using var scope = host.Services.CreateScope();
        var p = scope.ServiceProvider.GetRequiredService<BulkImportProcessor>();
        switch (message)
        {
            case SplitBulkImportFile m: await p.SplitAsync(m.FileId); break;
            case ReadBulkImportChunk m: await p.ReadAsync(m.ChunkId); break;
            case PlaceBulkImportChunk m: await p.PlaceAsync(m.ChunkId); break;
            case PromoteBulkImportFile m: await p.PromoteAsync(m.FileId); break;
            case CompileBulkImportFile m: await p.CompileAsync(m.FileId); break;
        }
    }

    /// <summary>Runs every message sent, and those they send, in order, until none is left.</summary>
    private async Task PumpAsync(WebApplicationFactory<Program> host)
    {
        for (var i = 0; i < 500 && _queue.Sent.TryDequeue(out var item); i++) await RunAsync(host, item.Message);
        _queue.Sent.Should().BeEmpty("every step finishes");
    }

    private async Task SplitAsync(WebApplicationFactory<Program> host)
    {
        foreach (var m in _queue.Take<SplitBulkImportFile>()) await RunAsync(host, m);
    }

    private static async Task<BulkImportBatchDto> BatchAsync(HttpClient author, Guid batchId) =>
        (await author.GetFromJsonAsync<BulkImportBatchDto>($"/api/bulk-imports/{batchId}"))!;

    private async Task<List<(string Type, string Text)>> BlocksAsync(Guid documentId)
    {
        await using var db = CreateDbContext();
        var blocks = await db.Blocks.AsNoTracking().Where(b => b.DocumentId == documentId).OrderBy(b => b.SortOrder).ToListAsync();
        return blocks.Select(b => (b.Type, b.Content.RootElement.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : b.Content.RootElement.GetRawText())).ToList();
    }

    private async Task<int> PagesChargedAsync(string userId)
    {
        await using var db = CreateDbContext();
        return await db.Operations.AsNoTracking().Where(o => o.UserId == userId && o.Kind == "import.pdf").SumAsync(o => o.Units ?? 0);
    }

    // ── no document while it imports ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task No_document_exists_while_the_file_imports()
    {
        var host = Host();
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);

        var file = await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(25));
        file.Pages.Should().Be(25);
        file.Status.Should().Be("queued");
        file.DocumentId.Should().BeNull();
        file.PreviewAvailable.Should().BeFalse();

        await SplitAsync(host);

        var after = (await BatchAsync(author, batchId)).Files.Single();
        after.DocumentId.Should().BeNull("the document is made when every chunk is finished");
        after.PromotedAt.Should().BeNull();
        after.Status.Should().Be("reading");
        after.Chunks.Total.Should().Be(3);
        (await DocumentsOfAsync(userId)).Should().BeEmpty();
        // Fair: two chunks sent to the ocr queue, the third waits its turn.
        var reads = _queue.Take<ReadBulkImportChunk>();
        reads.Should().HaveCount(2);

        // Read and staged: still no document, but a preview.
        foreach (var read in reads) await RunAsync(host, read);
        foreach (var place in _queue.Take<PlaceBulkImportChunk>()) await RunAsync(host, place);
        var staged = (await BatchAsync(author, batchId)).Files.Single();
        staged.DocumentId.Should().BeNull();
        staged.PreviewAvailable.Should().BeTrue();
        staged.Chunks.Done.Should().BeGreaterThan(0);
        (await DocumentsOfAsync(userId)).Should().BeEmpty("the import writes to staging, never to a document, while it runs");
        await using (var db = CreateDbContext())
            (await db.BulkImportStagedBlocks.AsNoTracking().CountAsync(b => b.FileId == file.FileId)).Should().BeGreaterThan(0);

        // Finished: one document, titled from the file name, and the staging gone.
        await PumpAsync(host);
        var done = (await BatchAsync(author, batchId)).Files.Single();
        done.Status.Should().Be("done");
        done.DocumentId.Should().NotBeNull();
        done.PromotedAt.Should().NotBeNull();
        (await DocumentsOfAsync(userId)).Should().Equal(done.DocumentId!.Value);
        var title = await author.GetFromJsonAsync<JsonElement>($"/api/documents/{done.DocumentId}");
        title.GetRawText().Should().Contain("Forkosh notebook 3");
        await using (var db = CreateDbContext())
        {
            (await db.BulkImportStagedBlocks.AsNoTracking().CountAsync(b => b.FileId == file.FileId)).Should().Be(0);
            (await db.BulkImportStagedAssets.AsNoTracking().CountAsync(a => a.FileId == file.FileId)).Should().Be(0);
        }
        // The progress that said "done" carried the document.
        _notifier.Progress.Last(p => p.FileId == file.FileId).DocumentId.Should().Be(done.DocumentId);
    }

    private async Task<List<Guid>> DocumentsOfAsync(string userId)
    {
        await using var db = CreateDbContext();
        return await db.Documents.AsNoTracking().IgnoreQueryFilters().Where(d => d.OwnerId == userId).Select(d => d.Id).ToListAsync();
    }

    // ── out of order, the order is still right ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Chunks_placed_out_of_order_give_the_pages_in_order()
    {
        var host = Host(maxChunksPerAuthor: 10);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(25));
        await SplitAsync(host);

        foreach (var read in _queue.Take<ReadBulkImportChunk>()) await RunAsync(host, read);
        var places = _queue.Take<PlaceBulkImportChunk>();
        places.Should().HaveCount(3);
        // The last chunk first, then the first, then the middle one: staged in that order, no document meanwhile.
        foreach (var i in new[] { 2, 0, 1 })
        {
            (await BatchAsync(author, batchId)).Files.Single().DocumentId.Should().BeNull();
            await RunAsync(host, places[i]);
        }
        // Every chunk staged: the promotion makes the document.
        var promotions = _queue.Take<PromoteBulkImportFile>();
        promotions.Should().NotBeEmpty();
        foreach (var m in promotions) await RunAsync(host, m);

        var file = (await BatchAsync(author, batchId)).Files.Single();
        file.Status.Should().Be("done");
        file.PagesDone.Should().Be(25);
        file.Chunks.Should().Be(new BulkImportChunkCountsDto(3, 3, 0));
        var texts = (await BlocksAsync(file.DocumentId!.Value)).Select(b => b.Text).ToList();
        texts.Should().NotContain(t => t.Contains("being read"));
        var order = new[] { "pages 1 to 10", "pages 11 to 20", "pages 21 to 25" }
            .Select(s => texts.FindIndex(t => t.Contains(s, StringComparison.OrdinalIgnoreCase))).ToList();
        order.Should().OnlyContain(i => i >= 0).And.BeInAscendingOrder();

        // The preamble: the first chunk placed set it; the others added only their own macros.
        await using var db = CreateDbContext();
        var preamble = await db.Documents.AsNoTracking().Where(d => d.Id == file.DocumentId).Select(d => d.CustomPreamble).SingleAsync();
        preamble.Should().Contain("\\newcommand{\\RR}");
        System.Text.RegularExpressions.Regex.Matches(preamble!, @"\\newcommand\{\\RR\}").Should().HaveCount(1);
        foreach (var first in new[] { 1, 11, 21 }) preamble.Should().Contain($"\\From{FakeMathpixClient.Word(first)}");
        // Charged its exact pages, once each chunk was read.
        (await PagesChargedAsync(userId)).Should().Be(25);
        _mathpix.Deletes.Should().Be(3, "each chunk is deleted at Mathpix once stored");
    }

    // ── per page ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Progress_counts_the_pages_mathpix_has_read_so_far()
    {
        var host = Host(maxChunksPerAuthor: 1);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(25));
        await SplitAsync(host);

        var seen = new List<int>();
        _mathpix.OnPoll = async (_, _) => seen.Add((await BatchAsync(author, batchId)).PagesDone);
        var read = _queue.Take<ReadBulkImportChunk>().Single();
        await RunAsync(host, read);
        _mathpix.OnPoll = null;

        // Polled three times: before Mathpix said anything (0), after it had read 4 pages (stored at the 2nd poll),
        // and then the 10.
        seen.Should().Equal(0, 0, 4);
        var batch = await BatchAsync(author, batchId);
        batch.PagesDone.Should().Be(10, "a chunk read and waiting for its place counts whole");
        batch.PagesTotal.Should().Be(25);

        foreach (var place in _queue.Take<PlaceBulkImportChunk>()) await RunAsync(host, place);
        (await BatchAsync(author, batchId)).PagesDone.Should().Be(10);
        var list = await author.GetFromJsonAsync<List<BulkImportBatchSummaryDto>>("/api/bulk-imports");
        list!.Single(b => b.BatchId == batchId).Should().Match<BulkImportBatchSummaryDto>(b =>
            b.PagesDone == 10 && b.PagesTotal == 25 && b.FilesTotal == 1 && b.FilesDone == 0 && b.Status == "processing");
    }

    // ── cache ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_same_pages_again_come_from_the_cache_with_no_call_and_no_charge()
    {
        var host = Host(maxChunksPerAuthor: 10);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var pdf = BulkImportTestKit.Pdf(15, salt: Guid.NewGuid().ToString());

        await UploadOkAsync(author, await NewBatchAsync(author), pdf);
        await PumpAsync(host);
        var submits = _mathpix.Submits;
        var charged = await PagesChargedAsync(userId);
        submits.Should().Be(2);
        charged.Should().Be(15);

        var again = await NewBatchAsync(author);
        await UploadOkAsync(author, again, pdf);
        await PumpAsync(host);

        _mathpix.Submits.Should().Be(submits, "no Mathpix call for pages read before");
        (await PagesChargedAsync(userId)).Should().Be(charged, "a cache hit is not charged");
        var file = (await BatchAsync(author, again)).Files.Single();
        file.Status.Should().Be("done");
        file.PagesDone.Should().Be(15);
        (await BlocksAsync(file.DocumentId!.Value)).Select(b => b.Text).Should().Contain(t => t.Contains("pages 11 to 15"));
        await using var db = CreateDbContext();
        (await db.BulkImportChunks.AsNoTracking().Where(c => c.FileId == file.FileId).AllAsync(c => c.FromCache && c.ChargedPages == 0)).Should().BeTrue();
    }

    // ── a failed chunk, then retry ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_failed_chunk_leaves_its_placeholder_the_rest_finish_and_retry_fills_it()
    {
        var host = Host(maxChunksPerAuthor: 10);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(25));
        _mathpix.FailFirstPages.Add(11);

        await PumpAsync(host);

        var file = (await BatchAsync(author, batchId)).Files.Single();
        file.Status.Should().Be("partial");
        file.DocumentId.Should().NotBeNull("promoted with what was read; the failed chunk is a placeholder");
        file.Chunks.Should().Be(new BulkImportChunkCountsDto(3, 2, 1));
        file.PagesDone.Should().Be(15);
        file.PagesFailed.Should().Be(10);
        file.Error.Should().Contain("Retry");
        var texts = (await BlocksAsync(file.DocumentId!.Value)).Select(b => b.Text).ToList();
        texts.Should().Contain("Pages 11\u201320 couldn't be read. Retry from Bulk import");
        texts.FindIndex(t => t.Contains("pages 1 to 10")).Should().BeLessThan(texts.FindIndex(t => t.StartsWith("Pages 11")));
        texts.FindIndex(t => t.StartsWith("Pages 11")).Should().BeLessThan(texts.FindIndex(t => t.Contains("pages 21 to 25")));
        (await PagesChargedAsync(userId)).Should().Be(15, "a failed chunk is never charged");
        await using (var db = CreateDbContext())
            (await db.BulkImportChunks.AsNoTracking().SingleAsync(c => c.FileId == file.FileId && c.FirstPage == 11)).Attempts.Should().Be(3);

        // Retry after promotion: only that chunk is read again, and its blocks take the placeholder's place in the document
        // (its version moved, the open editor told).
        long versionBefore;
        await using (var db = CreateDbContext())
            versionBefore = await db.Documents.AsNoTracking().Where(d => d.Id == file.DocumentId).Select(d => (long)d.Version).SingleAsync();
        _mathpix.FailFirstPages.Clear();
        var someoneElse = ClientOf(host, NewId("x"));
        (await someoneElse.PostAsync($"/api/bulk-imports/{batchId}/files/{file.FileId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var retry = await author.PostAsync($"/api/bulk-imports/{batchId}/files/{file.FileId}/retry", null);
        retry.StatusCode.Should().Be(HttpStatusCode.Accepted, await retry.Content.ReadAsStringAsync());
        (await BlocksAsync(file.DocumentId!.Value)).Select(b => b.Text).Should().Contain("Pages 11\u201320: being read\u2026");
        await PumpAsync(host);

        var done = (await BatchAsync(author, batchId)).Files.Single();
        done.Status.Should().Be("done");
        done.PagesDone.Should().Be(25);
        done.PagesFailed.Should().Be(0);
        var final = (await BlocksAsync(done.DocumentId!.Value)).Select(b => b.Text).ToList();
        new[] { "pages 1 to 10", "pages 11 to 20", "pages 21 to 25" }
            .Select(s => final.FindIndex(t => t.Contains(s))).Should().OnlyContain(i => i >= 0).And.BeInAscendingOrder();
        final.Should().NotContain(t => t.StartsWith("Pages 11"));
        (await PagesChargedAsync(userId)).Should().Be(25);
        done.DocumentId.Should().Be(file.DocumentId, "the retry fills the same document");
        (await DocumentsOfAsync(userId)).Should().ContainSingle();
        await using (var db = CreateDbContext())
            (await db.Documents.AsNoTracking().Where(d => d.Id == file.DocumentId).Select(d => (long)d.Version).SingleAsync())
                .Should().BeGreaterThan(versionBefore, "a change from outside moves the version, so an open editor rebases");
    }

    // ── deleting an old-flow file's document cancels what Mathpix hasn't got ───────────────────────────────────

    [Fact]
    public async Task Deleting_an_old_flow_files_document_cancels_the_chunks_not_yet_read_without_charge()
    {
        var host = Host();
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(35));
        await SplitAsync(host);
        var documentId = await AsSplitBeforeStagingAsync((await BatchAsync(author, batchId)).Files.Single().FileId);

        // The normal delete: to the Trash.
        (await author.DeleteAsync($"/api/documents/{documentId}")).IsSuccessStatusCode.Should().BeTrue();
        await PumpAsync(host);

        _mathpix.Submits.Should().Be(0, "nothing reaches Mathpix once the document is deleted");
        (await PagesChargedAsync(userId)).Should().Be(0);
        var file = (await BatchAsync(author, batchId)).Files.Single();
        file.Status.Should().Be("cancelled");
        file.Chunks.Total.Should().Be(4);
        await using var db = CreateDbContext();
        (await db.BulkImportChunks.AsNoTracking().Where(c => c.FileId == file.FileId).Select(c => c.Status).ToListAsync())
            .Should().OnlyContain(s => s == "cancelled");
        // And retry says why it can't.
        (await author.PostAsync($"/api/bulk-imports/{batchId}/files/{file.FileId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancelling_a_batch_stops_the_chunks_not_yet_sent_and_deletes_the_staging()
    {
        var host = Host(maxChunksPerAuthor: 1);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        _mathpix.FigureFirstPages.Add(1);
        var uploaded = await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(30, salt: Guid.NewGuid().ToString()));
        await SplitAsync(host);
        // The first chunk read and staged, with its figure; the others wait their turn.
        foreach (var read in _queue.Take<ReadBulkImportChunk>()) await RunAsync(host, read);
        foreach (var place in _queue.Take<PlaceBulkImportChunk>()) await RunAsync(host, place);
        List<string> stagedKeys;
        await using (var db = CreateDbContext())
        {
            (await db.BulkImportStagedBlocks.AsNoTracking().CountAsync(b => b.FileId == uploaded.FileId)).Should().BeGreaterThan(0);
            stagedKeys = await db.BulkImportStagedAssets.AsNoTracking().Where(a => a.FileId == uploaded.FileId).Select(a => a.StorageKey).ToListAsync();
        }
        stagedKeys.Should().ContainSingle("the figure is staged").Which.Should().StartWith($"bulk-staging/{uploaded.FileId:N}/");
        var storage = host.Services.GetRequiredService<Lilia.Core.Interfaces.IStorageService>();
        (await storage.DownloadAsync(stagedKeys[0])).Dispose();
        _queue.Sent.Clear(); // the next chunk's read, never run

        var cancel = await author.PostAsync($"/api/bulk-imports/{batchId}/cancel", null);
        cancel.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await PumpAsync(host);

        _mathpix.Submits.Should().Be(1, "only the chunk read before the cancel reached Mathpix");
        (await PagesChargedAsync(userId)).Should().Be(10, "nothing is charged for the pages not read");
        var file = (await BatchAsync(author, batchId)).Files.Single();
        file.Status.Should().Be("cancelled");
        file.DocumentId.Should().BeNull("no document is made for a cancelled file");
        (await DocumentsOfAsync(userId)).Should().BeEmpty();
        (await BatchAsync(author, batchId)).Status.Should().Be("cancelled");
        await using (var db = CreateDbContext())
        {
            (await db.BulkImportStagedBlocks.AsNoTracking().CountAsync(b => b.FileId == file.FileId)).Should().Be(0);
            (await db.BulkImportStagedAssets.AsNoTracking().CountAsync(a => a.FileId == file.FileId)).Should().Be(0);
            (await db.BulkImportChunks.AsNoTracking().Where(c => c.FileId == file.FileId).Select(c => c.Status).ToListAsync())
                .Should().OnlyContain(st => st == "cancelled");
        }
        var gone = async () => (await storage.DownloadAsync(stagedKeys[0])).Dispose();
        await gone.Should().ThrowAsync<Exception>("the staged figure is deleted with the staging");
        // The preview has nothing staged left.
        var preview = await author.GetFromJsonAsync<BulkImportPreviewDto>($"/api/bulk-imports/{batchId}/files/{file.FileId}/preview");
        preview!.Blocks.Should().OnlyContain(b => b.Type == "placeholder");
    }

    // ── the plan, and who may see a batch ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_pdf_the_plan_doesnt_cover_is_refused_before_anything_is_kept()
    {
        var host = Host(enforce: true);
        var userId = await AuthorAsync("free"); // Free: 10 PDF pages a month
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);

        var res = await UploadAsync(author, batchId, BulkImportTestKit.Pdf(25));

        res.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await res.Content.ReadAsStringAsync()).Should().Contain("PLAN_PDF_IMPORT");
        _queue.Sent.Should().BeEmpty();
        (await BatchAsync(author, batchId)).Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Pages_waiting_count_against_the_plan_so_many_files_cant_pass_it_one_by_one()
    {
        var host = Host(enforce: true);
        var userId = await AuthorAsync("pro"); // Pro: 100 PDF pages a month (until the owner sets it)
        int cap;
        await using (var db = CreateDbContext())
            cap = (await db.Plans.AsNoTracking().SingleAsync(p => p.Slug == "pro")).Caps.RootElement.GetProperty("pdfPagesPerMonth").GetInt32();
        if (cap < 0 || cap > 2000) return; // unlimited, or too many to drop here: nothing to check
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        var pdf = BulkImportTestKit.Pdf(25);

        // None of these is read yet, so none is charged: only the pages waiting stop the month being passed.
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < cap / 25 + 1; i++) statuses.Add((await UploadAsync(author, batchId, pdf)).StatusCode);

        // Accepted while the month's pages (and the day's credits: 10 a page) cover every page waiting, then refused.
        var accepted = statuses.TakeWhile(s => s == HttpStatusCode.Accepted).Count();
        accepted.Should().BeGreaterThan(0);
        (accepted * 25).Should().BeLessThanOrEqualTo(cap);
        statuses.Skip(accepted).Should().NotBeEmpty().And.OnlyContain(s => s == HttpStatusCode.PaymentRequired,
            "the pages queued count before they are read");
    }

    [Fact]
    public async Task A_bulk_file_is_one_import_whatever_its_chunks_and_a_normal_import_still_works()
    {
        // The review of API #675: every chunk made an IMPORT job, so a 30-page PDF used 3 of a plan's monthly imports and
        // could lock the author out of normal imports. A plan capped at 3 imports a month (Pro's caps otherwise, on a
        // plan this class doesn't use): the bulk file is 1, and a normal import after it still runs.
        var host = Host(enforce: true, maxChunksPerAuthor: 10);
        var userId = NewId("q");
        await SeedUserAsync(userId);
        await using (var db = CreateDbContext())
        {
            var pro = await db.Plans.AsNoTracking().SingleAsync(p => p.Slug == "pro");
            var capped = System.Text.Json.Nodes.JsonNode.Parse(pro.Caps.RootElement.GetRawText())!.AsObject();
            capped["maxImportsPerMonth"] = 3;
            capped["pdfPagesPerMonth"] = 1000;
            var plan = await db.Plans.SingleAsync(p => p.Slug == "student");
            plan.Caps = JsonDocument.Parse(capped.ToJsonString());
            plan.Features = JsonDocument.Parse(pro.Features.RootElement.GetRawText());
            db.UserPlans.Add(new UserPlan { Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, Status = "active", StartedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);

        await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(30, salt: Guid.NewGuid().ToString()));
        await PumpAsync(host);

        var file = (await BatchAsync(author, batchId)).Files.Single();
        file.Status.Should().Be("done");
        file.Chunks.Total.Should().Be(3);
        using (var scope = host.Services.CreateScope())
        {
            var usage = await scope.ServiceProvider.GetRequiredService<Lilia.Api.Services.IEntitlementService>().GetUsageAsync(userId);
            usage!.Imports.Used.Should().Be(1, "the bulk file is one import, its 3 chunks none");
        }
        await using (var db = CreateDbContext())
        {
            var jobs = await db.Jobs.AsNoTracking().Where(j => j.UserId == userId).ToListAsync();
            jobs.Count(j => j.JobType == JobTypes.ImportChunk).Should().Be(3);
            var import = jobs.Single(j => j.JobType == JobTypes.Import);
            import.Id.Should().Be(file.FileId);
            import.Status.Should().Be(JobStatus.Completed);
            import.DocumentId.Should().Be(file.DocumentId);
        }
        // The jobs list shows the file's import, not its chunks.
        var listed = await author.GetFromJsonAsync<JsonElement>("/api/lilia/jobs");
        listed.GetRawText().Should().NotContain(JobTypes.ImportChunk);

        var normal = await author.PostAsJsonAsync("/api/lilia/jobs/import",
            new { content = "\\documentclass{article}\n\\begin{document}\n\\section{After}\nA normal import after a bulk one.\n\\end{document}\n", format = "LATEX", filename = "after.tex", skipReview = true });
        normal.IsSuccessStatusCode.Should().BeTrue(await normal.Content.ReadAsStringAsync());
        using (var scope = host.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<Lilia.Api.Services.IEntitlementService>().GetUsageAsync(userId))!
                .Imports.Used.Should().Be(2);
    }

    [Fact]
    public async Task Only_the_author_sees_a_batch()
    {
        var host = Host();
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        var file = await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(3));
        var other = ClientOf(host, NewId("o"));

        (await other.GetAsync($"/api/bulk-imports/{batchId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await UploadAsync(other, batchId, BulkImportTestKit.Pdf(3))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"/api/bulk-imports/{batchId}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"/api/bulk-imports/{batchId}/files/{file.FileId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetFromJsonAsync<List<BulkImportBatchSummaryDto>>("/api/bulk-imports")).Should().BeEmpty();
        (await CreateAnonymousClient().GetAsync("/api/bulk-imports")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await author.GetAsync($"/api/bulk-imports/{batchId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Not_a_pdf_is_refused()
    {
        var host = Host();
        var author = ClientOf(host, await AuthorAsync());
        var batchId = await NewBatchAsync(author);

        var res = await UploadAsync(author, batchId, "hello, not a PDF"u8.ToArray(), "notes.pdf");

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _queue.Sent.Should().BeEmpty();
    }

    // ── the real queues ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Through_wolverines_queues_a_dropped_pdf_becomes_its_document()
    {
        var host = Host(realQueues: true);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(12, salt: Guid.NewGuid().ToString()));

        BulkImportBatchDto batch;
        var until = DateTime.UtcNow.AddSeconds(90);
        do
        {
            await Task.Delay(250);
            batch = await BatchAsync(author, batchId);
        } while (batch.Status != "done" && DateTime.UtcNow < until);

        batch.Status.Should().Be("done", JsonSerializer.Serialize(batch));
        var file = batch.Files.Single();
        file.PagesDone.Should().Be(12);
        var texts = (await BlocksAsync(file.DocumentId!.Value)).Select(b => b.Text).ToList();
        texts.FindIndex(t => t.Contains("pages 1 to 10")).Should().BeLessThan(texts.FindIndex(t => t.Contains("pages 11 to 12")));
    }
}
