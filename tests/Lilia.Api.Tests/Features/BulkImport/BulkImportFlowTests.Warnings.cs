using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Features.BulkImport.Contracts;
using Lilia.Api.Features.BulkImport.Services;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Features.BulkImport;

/// <summary>
/// A file's warnings, listed (21 Nov 2026, the launch bot's production retest: "It never names the warning or the block…
/// Could the warning name the block or page?"): <c>GET …/files/{fileId}/warnings</c> gives each one's kind, a plain
/// sentence, the chunk's pages and the block, its place in the document once the file is promoted. Author only.
/// </summary>
public partial class BulkImportFlowTests
{
    /// <summary>Formula text with a character no maths can hold: the import boxes it and warns (UnreadableSymbolReplaced).</summary>
    private const string UnsettableFormula = "Then $x = خ + 1$ holds.";

    private static async Task<BulkImportWarningListDto> WarningsAsync(HttpClient author, Guid batchId, Guid fileId)
    {
        var res = await author.GetAsync($"/api/bulk-imports/{batchId}/files/{fileId}/warnings");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<BulkImportWarningListDto>())!;
    }

    [Fact]
    public async Task Warnings_are_listed_with_their_kind_pages_and_block_and_the_block_is_placed_once_promoted()
    {
        var host = Host(maxChunksPerAuthor: 10);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        // Pages 1–10: page 2 low (a page warning). Pages 11–15: a formula with an unreadable symbol (a block warning).
        _mathpix.LinesJson = (first, pages) => FakeMathpixClient.Lines(pages, p => first == 1 && p == 2 ? 0.5 : 0.95);
        _mathpix.ExtraText = first => first == 11 ? UnsettableFormula : null;
        var uploaded = await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(15, salt: Guid.NewGuid().ToString()));

        // Every step but the promotion: the warnings are listed from the staging, their blocks not in a document yet.
        var held = new List<PromoteBulkImportFile>();
        for (var i = 0; i < 500 && _queue.Sent.TryDequeue(out var item); i++)
        {
            if (item.Message is PromoteBulkImportFile p) held.Add(p);
            else await RunAsync(host, item.Message);
        }
        held.Should().NotBeEmpty();
        var staged = await WarningsAsync(author, batchId, uploaded.FileId);
        staged.DocumentId.Should().BeNull();
        var stagedSymbol = staged.Warnings.Single(w => w.Kind == "UnreadableSymbolReplaced");
        stagedSymbol.BlockId.Should().NotBeNull("the staged block it is about");
        stagedSymbol.InDocument.Should().BeFalse();
        stagedSymbol.BlockNumber.Should().BeNull();
        await using (var db = CreateDbContext())
            (await db.BulkImportStagedBlocks.AsNoTracking().AnyAsync(b => b.Id == stagedSymbol.BlockId && b.FileId == uploaded.FileId))
                .Should().BeTrue("its id is the staged block's");

        foreach (var p in held) await RunAsync(host, p);
        await PumpAsync(host);

        var file = (await BatchAsync(author, batchId)).Files.Single();
        file.Status.Should().Be("done");
        file.DocumentId.Should().NotBeNull();
        var list = await WarningsAsync(author, batchId, file.FileId);
        list.FileId.Should().Be(file.FileId);
        list.DocumentId.Should().Be(file.DocumentId);
        list.Total.Should().Be(file.Warnings);
        list.Warnings.Should().HaveCount(file.Warnings, "every warning counted is listed");
        list.Truncated.Should().BeFalse();
        list.Warnings.Select(w => w.ChunkIndex).Should().BeInAscendingOrder("in page order");

        var low = list.Warnings.Single(w => w.Kind == "PDF.OCR_LOW_PRECISION");
        low.Page.Should().Be(2);
        (low.FirstPage, low.LastPage, low.ChunkIndex).Should().Be((1, 10, 0));
        low.BlockId.Should().BeNull("a page, not a block");
        low.InDocument.Should().BeFalse();
        low.Message.Should().Be("The reader was unsure (precision 0.50). Check it against the scan.");

        var symbol = list.Warnings.Single(w => w.Kind == "UnreadableSymbolReplaced");
        (symbol.FirstPage, symbol.LastPage, symbol.ChunkIndex).Should().Be((11, 15, 1));
        symbol.Page.Should().BeNull();
        symbol.Severity.Should().Be("warning");
        symbol.BlockId.Should().Be(stagedSymbol.BlockId, "the staged block's id is the document block's");
        symbol.BlockType.Should().Be("paragraph");
        symbol.InDocument.Should().BeTrue();
        symbol.Message.Should().StartWith("An unreadable symbol in a formula (U+062E (\u062E)) was replaced with a box (\u25A1). Check it against the scan.");
        symbol.Message.Should().NotStartWith("Block", "the chunk's own block number misled the tester; BlockNumber says where");
        await using (var db = CreateDbContext())
        {
            var blocks = await db.Blocks.AsNoTracking().Where(b => b.DocumentId == file.DocumentId).OrderBy(b => b.SortOrder)
                .Select(b => b.Id).ToListAsync();
            symbol.BlockNumber.Should().Be(blocks.IndexOf(symbol.BlockId!.Value) + 1, "its place in the whole document, from 1");
            symbol.BlockNumber.Should().BeGreaterThan(1, "after the first chunk's blocks");
        }

        // A block deleted since: still listed, no longer in the document.
        await using (var db = CreateDbContext())
            await db.Blocks.Where(b => b.Id == symbol.BlockId).ExecuteDeleteAsync();
        var after = (await WarningsAsync(author, batchId, file.FileId)).Warnings.Single(w => w.Kind == "UnreadableSymbolReplaced");
        after.InDocument.Should().BeFalse();
        after.BlockNumber.Should().BeNull();
    }

    [Fact]
    public async Task A_chunk_retried_into_the_document_lists_its_warnings_too()
    {
        var host = Host(maxChunksPerAuthor: 10);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        _mathpix.FailFirstPages.Add(11);
        _mathpix.ExtraText = first => first == 11 ? UnsettableFormula : null;
        await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(20, salt: Guid.NewGuid().ToString()));
        await PumpAsync(host);
        var file = (await BatchAsync(author, batchId)).Files.Single();
        file.Status.Should().Be("partial");
        (await WarningsAsync(author, batchId, file.FileId)).Warnings.Should().NotContain(w => w.Kind == "UnreadableSymbolReplaced");

        // Retried after promotion: placed straight into the document (not staged), and still listed.
        _mathpix.FailFirstPages.Clear();
        (await author.PostAsync($"/api/bulk-imports/{batchId}/files/{file.FileId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await PumpAsync(host);

        var done = (await BatchAsync(author, batchId)).Files.Single();
        done.Status.Should().Be("done");
        var symbol = (await WarningsAsync(author, batchId, done.FileId)).Warnings.Single(w => w.Kind == "UnreadableSymbolReplaced");
        (symbol.FirstPage, symbol.LastPage).Should().Be((11, 20));
        symbol.InDocument.Should().BeTrue();
        symbol.BlockNumber.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Only_the_author_sees_a_files_warnings()
    {
        var host = Host(maxChunksPerAuthor: 10);
        var userId = await AuthorAsync();
        var author = ClientOf(host, userId);
        var batchId = await NewBatchAsync(author);
        var uploaded = await UploadOkAsync(author, batchId, BulkImportTestKit.Pdf(5, salt: Guid.NewGuid().ToString()));

        var empty = await WarningsAsync(author, batchId, uploaded.FileId);
        empty.Warnings.Should().BeEmpty("nothing read yet");
        empty.Total.Should().Be(0);

        var someoneElse = ClientOf(host, NewId("x"));
        (await someoneElse.GetAsync($"/api/bulk-imports/{batchId}/files/{uploaded.FileId}/warnings")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var otherBatch = await NewBatchAsync(author);
        (await author.GetAsync($"/api/bulk-imports/{otherBatch}/files/{uploaded.FileId}/warnings")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await author.GetAsync($"/api/bulk-imports/{batchId}/files/{Guid.NewGuid()}/warnings")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Block 3 (paragraph): maths from the scan reader that LaTeX can't read ($x$) is printed as written. Check it against the scan.",
        "Maths from the scan reader that LaTeX can't read ($x$) is printed as written. Check it against the scan.", null)]
    [InlineData("Page 37: the reader was unsure (precision 0.62). Check it against the scan", "The reader was unsure (precision 0.62). Check it against the scan.", 37)]
    [InlineData("Unknown package foo was shimmed", "Unknown package foo was shimmed.", null)]
    public void A_warnings_message_loses_the_chunks_own_block_number(string raw, string plain, int? page)
    {
        BulkImportReadModel.PlainWarning(raw).Should().Be((plain, page));
    }
}
