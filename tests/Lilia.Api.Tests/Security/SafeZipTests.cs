using System.IO.Compression;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Security;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using static Lilia.Api.Tests.Security.ZipBombs;

namespace Lilia.Api.Tests.Security;

/// <summary>
/// Uploaded archives are read within ZipLimitsOptions: a zip bomb is refused
/// with UnsafeZipException (a 400 upstream), and a real file still reads.
/// </summary>
public class SafeZipTests
{
    private static EpubService Service() => new(NullLogger<EpubService>.Instance);

    [Fact]
    public async Task An_epub_whose_chapter_declares_60_MB_is_refused()
    {
        var bomb = Epub(60 * MB);
        bomb.Length.Should().BeLessThan((int)MB, "a bomb weighs little");
        var act = () => Service().ImportAsync(new MemoryStream(bomb));
        await act.Should().ThrowAsync<UnsafeZipException>();
    }

    [Fact]
    public async Task An_epub_whose_headers_lie_about_size_reads_no_more_than_declared()
    {
        // The chapter declares 1 KB and holds 60 MB. .NET stops at the declared
        // size, so the lie gets the author a 1 KB (broken) chapter, not 60 MB in
        // memory: the declared sizes CheckHeaders checks are what bound a read.
        var liar = LieAboutSize(Epub(60 * MB), "OEBPS/chapter.xhtml", 1024);
        var before = GC.GetTotalAllocatedBytes(precise: true);
        await Service().ImportAsync(new MemoryStream(liar));
        (GC.GetTotalAllocatedBytes(precise: true) - before).Should().BeLessThan(20 * MB);
    }

    [Fact]
    public void The_bounded_stream_stops_a_source_that_keeps_decompressing()
    {
        // The second layer, if a runtime or library ever stops truncating:
        // a stream that really yields 60 MB is cut off at the entry limit.
        var budget = new ZipBudget(ZipLimitsOptions.Default);
        using var s = SafeZip.Bound(new EndlessSpaces(60 * MB), "chapter.xhtml", budget);
        var act = () => { var buf = new byte[65536]; while (s.Read(buf, 0, buf.Length) > 0) { } };
        act.Should().Throw<UnsafeZipException>().WithMessage("*decompressed past*");
    }

    [Fact]
    public void The_archive_budget_stops_many_entries_that_are_each_within_the_limit()
    {
        var budget = new ZipBudget(new ZipLimitsOptions { MaxEntryBytes = 2 * MB, MaxTotalBytes = 3 * MB });
        var buf = new byte[65536];
        var act = () =>
        {
            for (var i = 0; i < 3; i++)
            {
                using var s = SafeZip.Bound(new EndlessSpaces((long)(1.5 * MB)), $"part{i}", budget);
                while (s.Read(buf, 0, buf.Length) > 0) { }
            }
        };
        act.Should().Throw<UnsafeZipException>().WithMessage("*in total*");
    }

    [Fact]
    public async Task A_real_exported_book_still_imports()
    {
        var epub = Service();
        var book = await epub.ExportAsync(
            [new Lilia.Core.Entities.Block { Type = "paragraph", Content = System.Text.Json.JsonDocument.Parse("{\"text\":\"The tide came in.\"}") }],
            new Lilia.Core.Models.Epub.EpubExportOptions(Title: "Harbour"));
        var (metadata, blocks, _) = await epub.ImportAsync(new MemoryStream(book));
        metadata.Title.Should().Be("Harbour");
        blocks.Should().NotBeEmpty();
    }

    [Fact]
    public void Too_many_entries_are_refused()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            for (var i = 0; i < 2_001; i++) zip.CreateEntry($"f{i}.txt");
        ms.Position = 0;
        using var read = new ZipArchive(ms, ZipArchiveMode.Read);
        var act = () => SafeZip.CheckHeaders(read);
        act.Should().Throw<UnsafeZipException>().WithMessage("*2001 entries*");
    }

    [Fact]
    public void A_total_over_the_limit_is_refused_even_when_each_entry_is_within_it()
    {
        var limits = new ZipLimitsOptions { MaxEntryBytes = 2 * MB, MaxTotalBytes = 3 * MB, MaxCompressionRatio = 10_000 };
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            for (var i = 0; i < 3; i++) PutPadded(zip, $"part{i}.xml", (long)(1.5 * MB));
        ms.Position = 0;
        var act = () => SafeZip.Verify(ms, limits);
        act.Should().Throw<UnsafeZipException>().WithMessage("*in total*");
    }

    [Fact]
    public void An_entry_that_compresses_1000_to_1_is_refused()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            PutPadded(zip, "padding.xml", 10 * MB);
        ms.Position = 0;
        var act = () => SafeZip.Verify(ms);
        act.Should().Throw<UnsafeZipException>().WithMessage("*compresses*");
    }

    [Fact]
    public void A_latex_project_with_a_bomb_file_is_refused_and_a_big_honest_one_is_skipped()
    {
        var extractor = new LatexProjectExtractor();

        // Over the 5 MB per-file cap: skipped with a notice, as before.
        var big = extractor.Extract(LatexProject("figures/huge.eps", 6 * MB));
        big.Notices.Should().Contain(n => n.Contains("huge.eps"));

        // Under the cap but 4 MB of padding compressing ~1000:1: refused.
        var act = () => extractor.Extract(LatexProject("figures/bomb.eps", 4 * MB));
        act.Should().Throw<UnsafeZipException>().WithMessage("*compresses*");
    }

    [Fact]
    public async Task A_docx_bomb_fails_the_import_with_the_plain_message()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bomb-{Guid.NewGuid():N}.docx");
        await File.WriteAllBytesAsync(path, Docx(60 * MB));
        try
        {
            var result = await new DocxImportService().ImportAsync(path);
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Be(UnsafeZipException.UserMessage);
        }
        finally { File.Delete(path); }
    }

    /// <summary>A read-only stream of spaces, like an entry that keeps decompressing.</summary>
    private sealed class EndlessSpaces(long length) : Stream
    {
        private long _pos;
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - _pos);
            if (n <= 0) return 0;
            buffer.AsSpan(offset, n).Fill((byte)' ');
            _pos += n;
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
