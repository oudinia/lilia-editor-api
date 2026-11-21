using FluentAssertions;
using Lilia.Api.Features.BulkImport;
using Lilia.Api.Features.BulkImport.Services;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Features.BulkImport;

/// <summary>
/// Bulk import's splitting (21 Nov 2026): a PDF cut every <c>BulkImport:PagesPerChunk</c> pages (10), the last chunk
/// shorter, each chunk its own PDF on disk holding exactly its pages. PdfPig in process; poppler's pdfseparate and
/// pdfunite when PdfPig can't. And the small rules around it: the chunk's cache key, a file's status from its chunks,
/// the macros a later chunk adds.
/// </summary>
public class BulkImportSplitTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lilia-bulk-split-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(1, new[] { 1 })]
    [InlineData(10, new[] { 10 })]
    [InlineData(11, new[] { 10, 1 })]
    [InlineData(150, new[] { 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10 })]
    [InlineData(25, new[] { 10, 10, 5 })]
    public void Ranges_cut_every_ten_pages_the_last_shorter(int pages, int[] sizes)
    {
        var ranges = PdfSplitter.Ranges(pages, 10);

        ranges.Select(r => r.Count).Should().Equal(sizes);
        ranges.First().First.Should().Be(1);
        ranges.Last().Last.Should().Be(pages);
        ranges.Select(r => r.Index).Should().Equal(Enumerable.Range(0, sizes.Length));
        ranges.Zip(ranges.Skip(1)).Should().OnlyContain(p => p.Second.First == p.First.Last + 1, "no page is lost or read twice");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(150)]
    public async Task Each_chunk_holds_exactly_its_pages(int pages)
    {
        Directory.CreateDirectory(_dir);
        var source = Path.Combine(_dir, "source.pdf");
        await File.WriteAllBytesAsync(source, BulkImportTestKit.Pdf(pages));
        var ranges = PdfSplitter.Ranges(pages, 10);

        var paths = await new PdfSplitter(new BulkImportOptions(), NullLogger<PdfSplitter>.Instance)
            .SplitAsync(source, ranges, Path.Combine(_dir, "chunks"), CancellationToken.None);

        paths.Should().HaveCount(ranges.Count);
        foreach (var (path, range) in paths.Zip(ranges))
            BulkImportTestKit.PageNumbers(path).Should().Equal(Enumerable.Range(range.First, range.Count));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(25)]
    public async Task Poppler_the_fallback_cuts_the_same_chunks(int pages)
    {
        if (!File.Exists("/usr/bin/pdfseparate") || !File.Exists("/usr/bin/pdfunite")) return; // poppler isn't installed here
        Directory.CreateDirectory(_dir);
        var source = Path.Combine(_dir, "source.pdf");
        await File.WriteAllBytesAsync(source, BulkImportTestKit.Pdf(pages));
        var ranges = PdfSplitter.Ranges(pages, 10);

        var paths = await new PdfSplitter(new BulkImportOptions(), NullLogger<PdfSplitter>.Instance)
            .SplitWithPopplerAsync(source, ranges, Path.Combine(_dir, "poppler"), CancellationToken.None);

        foreach (var (path, range) in paths.Zip(ranges))
            BulkImportTestKit.PageNumbers(path).Should().Equal(Enumerable.Range(range.First, range.Count));
    }

    [Fact]
    public async Task A_pdf_pdfpig_cant_read_is_split_by_poppler_or_refused()
    {
        // Not a PDF PdfPig opens: poppler is tried, and it fails too, so the split fails (the file is then "failed").
        Directory.CreateDirectory(_dir);
        var source = Path.Combine(_dir, "broken.pdf");
        await File.WriteAllTextAsync(source, "%PDF-1.4 not really");
        var split = () => new PdfSplitter(new BulkImportOptions(), NullLogger<PdfSplitter>.Instance)
            .SplitAsync(source, PdfSplitter.Ranges(3, 10), Path.Combine(_dir, "chunks"), CancellationToken.None);

        await split.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void The_same_pages_of_the_same_file_have_the_same_cache_key()
    {
        var a = BulkImportProcessor.ChunkHash("abc", 11, 20);
        a.Should().Be(BulkImportProcessor.ChunkHash("abc", 11, 20)).And.HaveLength(64);
        a.Should().NotBe(BulkImportProcessor.ChunkHash("abc", 21, 30));
        a.Should().NotBe(BulkImportProcessor.ChunkHash("abd", 11, 20));
    }

    [Theory]
    [InlineData(new[] { "done", "done" }, "done")]
    [InlineData(new[] { "done", "failed" }, "partial")]
    [InlineData(new[] { "failed", "failed" }, "failed")]
    [InlineData(new[] { "done", "cancelled" }, "partial")]
    [InlineData(new[] { "cancelled", "cancelled" }, "cancelled")]
    [InlineData(new[] { "done", "reading" }, "reading")]
    [InlineData(new[] { "failed", "queued" }, "reading")]
    public void A_files_status_follows_its_chunks(string[] chunks, string file) =>
        BulkImportProcessor.FileStatusOf(chunks).Should().Be(file);

    [Fact]
    public void Placeholders_name_their_pages()
    {
        var chunk = new BulkImportChunk { FirstPage = 31, LastPage = 40 };
        BulkImportProcessor.ReadingText(chunk).Should().Be("Pages 31–40: being read…");
        BulkImportProcessor.FailedText(chunk).Should().Be("Pages 31–40 couldn't be read. Retry from Bulk import");
        BulkImportProcessor.ReadingText(new BulkImportChunk { FirstPage = 7, LastPage = 7 }).Should().StartWith("Page 7:");
    }

    [Fact]
    public void A_later_chunk_adds_only_the_macros_the_document_lacks()
    {
        const string existing = "\\newcommand{\\RR}{\\mathbb{R}}\n\\DeclareMathOperator{\\Tr}{tr}";
        const string chunk = """
            \documentclass{article}
            \newcommand{\RR}{\mathbb{R}^n}
            \newcommand{\NN}{\mathbb{N}}
            \DeclareMathOperator{\Tr}{Tr}
            \newtheorem{thm}{Theorem}
            \begin{document}x\end{document}
            """;

        var missing = LatexPreambleExtractor.MissingDefinitions(existing, chunk);

        missing.Should().Contain("\\newcommand{\\NN}{\\mathbb{N}}").And.Contain("\\newtheorem{thm}{Theorem}");
        missing.Should().NotContain("\\RR").And.NotContain("\\Tr");
        LatexPreambleExtractor.MissingDefinitions(existing + "\n" + missing, chunk).Should().BeNull();
    }
}
