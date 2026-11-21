using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Features.BulkImport.Contracts;
using Lilia.Api.Features.BulkImport.Services;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Features.BulkImport;

/// <summary>
/// The bulk import timings and precision score (21 Nov 2026, the owner: "how long it takes to actually compile, import and
/// convert… timed exactly", and "how sure the OCR is"): each stage's milliseconds on the rows and in the file's
/// <c>timings</c>, a cache hit with no Mathpix time, the precision from Mathpix's lines.json (a low page is a warning), a
/// lines.json that fails leaving no score and the import whole, the compile once a file is done, and the staff-only
/// stats. Mathpix and the compile are fakes: no network, no TeX.
/// </summary>
public partial class BulkImportFlowTests
{
    private async Task<(BulkImportFileDto File, List<BulkImportChunk> Chunks, BulkImportFile Row)> FileStateAsync(HttpClient author, Guid batchId)
    {
        var dto = (await BatchAsync(author, batchId)).Files.Single();
        await using var db = CreateDbContext();
        var row = await db.BulkImportFiles.AsNoTracking().SingleAsync(f => f.Id == dto.FileId);
        var chunks = await db.BulkImportChunks.AsNoTracking().Where(c => c.FileId == dto.FileId).OrderBy(c => c.Index).ToListAsync();
        return (dto, chunks, row);
    }

    // ── timings at each stage ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Every_stage_is_timed_on_the_rows_and_in_the_files_timings()
    {
        var host = Host(maxChunksPerAuthor: 10);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        var pdf = BulkImportTestKit.Pdf(25, salt: Guid.NewGuid().ToString());
        // Mathpix takes a little time per poll, so its time is measurably more than nothing.
        _mathpix.OnPoll = (_, _) => Task.Delay(5);

        var uploaded = await UploadOkAsync(author, batchId, pdf);
        uploaded.Timings!.UploadMs.Should().NotBeNull("the upload is timed as soon as the file is kept");
        // Every step but the compile, which is held back here to show it comes after the import, on its own.
        var held = new List<CompileBulkImportFile>();
        for (var i = 0; i < 500 && _queue.Sent.TryDequeue(out var item); i++)
        {
            if (item.Message is CompileBulkImportFile c) held.Add(c);
            else await RunAsync(host, item.Message);
        }
        _mathpix.OnPoll = null;

        var (file, chunks, row) = await FileStateAsync(author, batchId);
        file.Status.Should().Be("done");

        // The file: upload (with its bytes), split, first page shown, whole.
        row.UploadMs.Should().BeGreaterThanOrEqualTo(0);
        row.SizeBytes.Should().Be(pdf.Length);
        row.SplitMs.Should().BeGreaterThanOrEqualTo(0);
        row.FirstPageMs.Should().BeGreaterThanOrEqualTo(row.UploadMs!.Value, "the first page counts from the upload's start");
        row.TotalMs.Should().BeGreaterThanOrEqualTo(row.FirstPageMs!.Value);
        // The promotion: the staging made into the document in one go, timed, and inside the whole.
        row.PromoteMs.Should().NotBeNull().And.BeGreaterThanOrEqualTo(0);
        row.TotalMs.Should().BeGreaterThanOrEqualTo(row.PromoteMs!.Value);
        row.PromotedAt.Should().NotBeNull();
        // Each chunk: its wait, Mathpix (submit → completed), the tex.zip, and the place split in parts.
        chunks.Should().HaveCount(3).And.OnlyContain(c =>
            c.QueueWaitMs >= 0 && c.MathpixMs >= 10 && c.TexZipMs >= 0
            && c.PlaceMs >= 0 && c.PlaceParseMs >= 0 && c.PlaceFiguresMs >= 0 && c.PlaceWriteMs >= 0 && !c.FromCache);
        chunks.Should().OnlyContain(c => c.PlaceParseMs + c.PlaceFiguresMs + c.PlaceWriteMs <= c.PlaceMs + 5,
            "the parts are within the whole");

        // The DTO's averages.
        var t = file.Timings!;
        t.SplitMs.Should().Be(row.SplitMs);
        t.FirstPageMs.Should().Be(row.FirstPageMs);
        t.TotalMs.Should().Be(row.TotalMs);
        t.PromoteMs.Should().Be(row.PromoteMs);
        t.MathpixMsPerPage.Should().Be((int)Math.Round(chunks.Sum(c => (double)c.MathpixMs!.Value) / 25));
        t.PlaceMsPerChunk.Should().NotBeNull();
        t.QueueWaitMsPerChunk.Should().NotBeNull();
        t.CachedChunks.Should().Be(0);
        if (row.TotalMs > 0) t.PagesPerMinute.Should().BeApproximately(25 / (row.TotalMs!.Value / 60000.0), 0.1);

        // The compile: asked once the file is done, not run inside the import.
        t.CompileMs.Should().BeNull("the compile runs on its own queue, after the import");
        _compiler.Compiled.Should().BeEmpty();
        held.Should().ContainSingle().Which.FileId.Should().Be(file.FileId);
        await RunAsync(host, held[0]);
        (await BatchAsync(author, batchId)).Files.Single().Timings!.CompileMs.Should().NotBeNull();
    }

    // ── a cache hit ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_cache_hit_says_so_and_records_no_mathpix_time()
    {
        var host = Host(maxChunksPerAuthor: 10);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var pdf = BulkImportTestKit.Pdf(15, salt: Guid.NewGuid().ToString());
        _mathpix.LinesJson = (first, pages) => FakeMathpixClient.Lines(pages, p => first == 1 && p == 2 ? 0.5 : 0.9);

        await UploadOkAsync(author, await NewBatchAsync(author), pdf);
        await PumpAsync(host);
        var linesCalls = _mathpix.LinesCalls;
        linesCalls.Should().Be(2);

        var again = await NewBatchAsync(author);
        await UploadOkAsync(author, again, pdf);
        await PumpAsync(host);

        var (file, chunks, _) = await FileStateAsync(author, again);
        file.Status.Should().Be("done");
        chunks.Should().OnlyContain(c => c.FromCache && c.MathpixMs == null && c.TexZipMs == null,
            "the time a cache hit saves is not Mathpix's");
        chunks.Should().OnlyContain(c => c.QueueWaitMs != null && c.PlaceMs != null, "the rest is still timed");
        file.Timings!.CachedChunks.Should().Be(2);
        file.Timings.MathpixMsPerPage.Should().BeNull("no page was read by Mathpix");
        _mathpix.LinesCalls.Should().Be(linesCalls, "no lines.json for a cache hit");
        // The precision came with the cached reading.
        file.Precision!.LowPages.Should().Equal(2);
    }

    // ── the precision score ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_precision_score_comes_from_lines_json_and_a_low_page_is_a_warning()
    {
        var host = Host(maxChunksPerAuthor: 10);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        // The second chunk (pages 11–15) is the fixture: its 2nd page (the file's 12th) low, at 0.63.
        _mathpix.LinesJson = (first, pages) => first == 11 ? MathpixPrecisionTests.Fixture : FakeMathpixClient.Lines(pages, _ => 0.95);

        await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(15, salt: Guid.NewGuid().ToString()));
        await PumpAsync(host);

        var (file, chunks, row) = await FileStateAsync(author, batchId);
        file.Status.Should().Be("done");
        // Pages 1–10 at 0.95; 11, 12, 13 at 0.96, 0.63, 0.85 (the fixture; its 4th page has no text, no score).
        var expected = Math.Round((10 * 0.95 + 0.96 + 0.63 + 0.85) / 13, 3);
        file.Precision!.Score.Should().BeApproximately(expected, 0.001);
        file.Precision.LowPages.Should().Equal(12);
        row.LowPrecisionPages.Should().Be(1);
        chunks[1].Precision.Should().BeApproximately(0.813, 0.001);
        Lilia.Import.Services.MathpixPrecision.Parse(chunks[1].PrecisionPages).Select(p => p.Page).Should().Equal(11, 12, 13);

        // The warning, with the import's others: counted on the file, and on the chunk's review session.
        file.Warnings.Should().BeGreaterThanOrEqualTo(1);
        await using var db = CreateDbContext();
        var warning = await db.ImportDiagnostics.AsNoTracking()
            .Where(d => d.Code == "PDF.OCR_LOW_PRECISION" && d.Session.DocumentId == file.DocumentId)
            .Select(d => d.Message).ToListAsync();
        warning.Should().Equal("Page 12: the reader was unsure (precision 0.63). Check it against the scan");
    }

    [Fact]
    public async Task A_lines_json_that_fails_leaves_no_score_and_the_chunk_still_succeeds()
    {
        var host = Host(maxChunksPerAuthor: 10);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        _mathpix.LinesFail = true;

        await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(12, salt: Guid.NewGuid().ToString()));
        await PumpAsync(host);

        var (file, chunks, row) = await FileStateAsync(author, batchId);
        file.Status.Should().Be("done", "the score is never a reason to fail an import");
        file.PagesDone.Should().Be(12);
        file.Precision.Should().BeNull();
        row.Precision.Should().BeNull();
        chunks.Should().OnlyContain(c => c.Status == "done" && c.PrecisionPages == null && c.Precision == null && c.Attempts == 1);
        _mathpix.LinesCalls.Should().Be(2);
    }

    // ── the compile once a file is done ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_done_file_is_compiled_once_in_the_background_and_timed()
    {
        var host = Host(maxChunksPerAuthor: 10);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(12, salt: Guid.NewGuid().ToString()));
        _compiler.Delay = TimeSpan.FromMilliseconds(40);

        await PumpAsync(host); // runs the compile message too

        var (file, _, row) = await FileStateAsync(author, batchId);
        _compiler.Compiled.Should().ContainSingle().Which.Should().Be(file.DocumentId!.Value);
        row.CompileOk.Should().BeTrue();
        row.CompileMs.Should().BeGreaterThanOrEqualTo(30, "the fake compile takes 40 ms (timers round)");
        row.CompileEngine.Should().Be("typst");
        row.CompileError.Should().BeNull();
        file.Timings!.CompileMs.Should().Be(row.CompileMs);
        file.Timings.CompileOk.Should().BeTrue();

        // Once: the same message again compiles nothing more.
        await RunAsync(host, new CompileBulkImportFile(file.FileId));
        _compiler.Compiled.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_compile_that_fails_records_its_first_line_and_a_failed_file_isnt_compiled()
    {
        var host = Host(maxChunksPerAuthor: 10);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        _compiler.Result = new Lilia.Api.Services.BulkImportCompileResult(false, "pdflatex",
            "\n  ! Undefined control sequence \\foo.\nl.12 \\foo\n");
        await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(5, salt: Guid.NewGuid().ToString()));
        await PumpAsync(host);

        var (_, _, row) = await FileStateAsync(author, batchId);
        row.CompileOk.Should().BeFalse();
        row.CompileEngine.Should().Be("pdflatex");
        row.CompileError.Should().Be("! Undefined control sequence \\foo.");
        row.CompileMs.Should().NotBeNull();

        // A file whose every page failed has nothing to compile.
        var failedBatch = await NewBatchAsync(author);
        _mathpix.FailFirstPages.Add(1);
        await UploadOkAsync(author, failedBatch, BulkImportTestKit.Pdf(4, salt: Guid.NewGuid().ToString()));
        await PumpAsync(host);
        var (failed, _, failedRow) = await FileStateAsync(author, failedBatch);
        failed.Status.Should().Be("failed");
        failedRow.TotalMs.Should().NotBeNull("a failed file is finished: its whole is timed");
        failedRow.CompileOk.Should().BeNull();
        _compiler.Compiled.Should().HaveCount(1);
    }

    // ── the staff's stats ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_stats_are_staff_only()
    {
        var staffId = NewId("s");
        await SeedUserAsync(staffId);
        var host = Host(staff: staffId);
        var author = ClientOf(host, await AuthorAsync());
        var staff = ClientOf(host, staffId);

        (await author.GetAsync("/api/bulk-imports/stats")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CreateAnonymousClient().GetAsync("/api/bulk-imports/stats")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var res = await staff.GetAsync("/api/bulk-imports/stats?days=3");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var stats = (await res.Content.ReadFromJsonAsync<BulkImportStatsDto>())!;
        stats.Days.Should().Be(3);
        stats.Stages.Select(s => s.Stage).Should().Contain(["upload", "split", "queueWait", "mathpix", "mathpixPerPage", "texZip",
            "place", "placeParse", "placeFigures", "placeWrite", "firstPage", "promote", "total", "compile", "pagesPerMinute", "precision"]);
    }

    [Fact]
    public async Task The_stats_give_p50_p90_and_max_per_stage_over_the_window()
    {
        // A window of its own, years back, so the other tests' imports aren't in it.
        var now = new DateTime(2001, 3, 10, 12, 0, 0, DateTimeKind.Utc);
        var userId = NewId("w");
        await SeedUserAsync(userId);
        await using (var db = CreateDbContext())
        {
            var batch = new BulkImportBatch { Id = Guid.NewGuid(), UserId = userId, Status = "done", CreatedAt = now.AddDays(-1) };
            db.BulkImportBatches.Add(batch);
            var uploads = new[] { 100, 200, 300, 400, 500 };
            for (var i = 0; i < uploads.Length; i++)
            {
                var file = new BulkImportFile
                {
                    Id = Guid.NewGuid(), BatchId = batch.Id, UserId = userId, OriginalName = $"f{i}.pdf", StorageKey = "k", Pages = 10,
                    Status = "done", UploadMs = uploads[i], TotalMs = 60_000 * (i + 1), CompileMs = i == 0 ? 2000 : null,
                    PromoteMs = 10 * (i + 1),
                    CompileOk = i == 0 ? true : null, Precision = 0.9, LowPrecisionPages = i,
                    CreatedAt = now.AddHours(-1 - i),
                };
                db.BulkImportFiles.Add(file);
                db.BulkImportChunks.Add(new BulkImportChunk
                {
                    Id = Guid.NewGuid(), FileId = file.Id, UserId = userId, Index = 0, FirstPage = 1, LastPage = 10, Status = "done",
                    MathpixMs = 1000 * (i + 1), QueueWaitMs = 10, PlaceMs = 50, FromCache = false, CreatedAt = file.CreatedAt,
                });
            }
            // Outside the window: before it, and after "now".
            db.BulkImportFiles.Add(new BulkImportFile { Id = Guid.NewGuid(), BatchId = batch.Id, UserId = userId, OriginalName = "old.pdf", StorageKey = "k", Pages = 1, UploadMs = 99_999, CreatedAt = now.AddDays(-8) });
            db.BulkImportFiles.Add(new BulkImportFile { Id = Guid.NewGuid(), BatchId = batch.Id, UserId = userId, OriginalName = "new.pdf", StorageKey = "k", Pages = 1, UploadMs = 99_999, CreatedAt = now.AddDays(1) });
            await db.SaveChangesAsync();
        }

        await using var read = CreateDbContext();
        var stats = await BulkImportStats.ComputeAsync(read, 7, now);

        stats.Files.Should().Be(5);
        stats.Chunks.Should().Be(5);
        stats.Pages.Should().Be(50);
        stats.CompilesOk.Should().Be(1);
        stats.LowPrecisionPages.Should().Be(10);
        var upload = stats.Stages.Single(s => s.Stage == "upload");
        upload.Should().Be(new BulkImportStageStatsDto("upload", "ms", 5, 300, 460, 100, 500), "interpolated as percentile_cont");
        var perPage = stats.Stages.Single(s => s.Stage == "mathpixPerPage");
        perPage.P50.Should().Be(300);
        perPage.Max.Should().Be(500);
        var perMinute = stats.Stages.Single(s => s.Stage == "pagesPerMinute");
        perMinute.Max.Should().Be(10, "10 pages in 1 minute");
        perMinute.Min.Should().Be(2, "10 pages in 5 minutes");
        stats.Stages.Single(s => s.Stage == "compile").Count.Should().Be(1);
        stats.Stages.Single(s => s.Stage == "promote").Should().Be(new BulkImportStageStatsDto("promote", "ms", 5, 30, 46, 10, 50));
        stats.Stages.Single(s => s.Stage == "split").Should().Be(new BulkImportStageStatsDto("split", "ms", 0, null, null, null, null));
    }
}
