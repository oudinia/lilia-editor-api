using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Lilia.Core.Security;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Shared;

/// <summary>
/// launch-bot #221 (23 Nov 2026): a project zip sent through the connector as base64 stored a PNG with 2 bytes changed,
/// which printed half black. Each file is now checked against the zip's CRC-32, and a damaged one refuses the import.
/// </summary>
public class ZipCrcTests
{
    private static readonly byte[] Image = Enumerable.Range(0, 758).Select(i => (byte)(i * 7 % 251)).ToArray();

    private static byte[] Project()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("main.tex", CompressionLevel.NoCompression).Open(), Encoding.UTF8))
                w.Write("\\documentclass{article}\\begin{document}\\includegraphics{figure1}\\end{document}");
            using (var s = zip.CreateEntry("figure1.png", CompressionLevel.NoCompression).Open())
                s.Write(Image);
        }
        return ms.ToArray();
    }

    [Fact]
    public void The_checksum_is_the_zip_format_s()
    {
        SafeZip.Crc32("123456789"u8).Should().Be(0xCBF43926u);
    }

    [Fact]
    public void An_intact_project_reads_and_its_image_is_byte_for_byte()
    {
        var extracted = new LatexProjectExtractor().Extract(Project());
        extracted.Files.Single(f => f.Path.EndsWith("figure1.png")).Bytes.Should().Equal(Image);
    }

    [Fact]
    public void A_project_with_two_bytes_changed_in_its_image_is_refused()
    {
        var zip = Project();
        // The image's bytes sit in the zip as they are (stored): change two of them, as one wrong base64 character does.
        var at = zip.AsSpan().IndexOf(Image.AsSpan(380, 40)) + 20;
        at.Should().BeGreaterThan(20);
        zip[at] ^= 0x55;
        zip[at + 1] ^= 0xAA;

        var act = () => new LatexProjectExtractor().Extract(zip);
        act.Should().Throw<InvalidDataException>().WithMessage("*figure1.png arrived damaged*");
    }
}
