using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.AI;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Unit tests for <see cref="AskAttachmentBuilder"/> — verifies each attachment
/// type maps to the right model content: images/PDFs → native DataContent,
/// docx/text → extracted/inlined TextContent, and oversized/undecodable/
/// unsupported items → an explanatory note (never silently dropped).
/// </summary>
public class AskAttachmentBuilderTests
{
    private static readonly AttachmentLimits Limits = new(
        MaxImageBytes: 5 * 1024 * 1024, MaxPdfBytes: 10 * 1024 * 1024,
        MaxFileBytes: 10 * 1024 * 1024, MaxTextChars: 100_000);

    private static AskAttachment Att(string name, string media, byte[] bytes) =>
        new(name, media, Convert.ToBase64String(bytes));

    [Fact]
    public void Image_BecomesNativeDataContent()
    {
        var parts = AskAttachmentBuilder.Build(
            new[] { Att("chart.png", "image/png", new byte[] { 1, 2, 3, 4 }) }, Limits);

        parts.Should().ContainSingle();
        var data = parts[0].Should().BeOfType<DataContent>().Subject;
        data.MediaType.Should().Be("image/png");
    }

    [Fact]
    public void Pdf_ByExtension_BecomesNativeDataContent()
    {
        var parts = AskAttachmentBuilder.Build(
            new[] { Att("paper.pdf", "application/octet-stream", new byte[] { 37, 80, 68, 70 }) }, Limits);

        parts.Should().ContainSingle();
        parts[0].Should().BeOfType<DataContent>().Which.MediaType.Should().Be("application/pdf");
    }

    [Fact]
    public void TextFile_IsInlinedFenced()
    {
        var csv = Encoding.UTF8.GetBytes("player,goals\nMessi,7\n");
        var parts = AskAttachmentBuilder.Build(new[] { Att("scorers.csv", "text/csv", csv) }, Limits);

        var text = parts.Should().ContainSingle().Which.Should().BeOfType<TextContent>().Subject.Text;
        text.Should().Contain("scorers.csv").And.Contain("Messi,7");
    }

    [Fact]
    public void Docx_IsTextExtracted()
    {
        var parts = AskAttachmentBuilder.Build(
            new[] { Att("notes.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", MakeDocx("Hello from Word")) },
            Limits);

        var text = parts.Should().ContainSingle().Which.Should().BeOfType<TextContent>().Subject.Text;
        text.Should().Contain("notes.docx").And.Contain("Hello from Word");
    }

    [Fact]
    public void OversizedImage_BecomesNote_NotDropped()
    {
        var tiny = new AttachmentLimits(MaxImageBytes: 2, MaxPdfBytes: 10, MaxFileBytes: 10, MaxTextChars: 100);
        var parts = AskAttachmentBuilder.Build(
            new[] { Att("big.png", "image/png", new byte[] { 1, 2, 3, 4, 5 }) }, tiny);

        parts.Should().ContainSingle();
        parts[0].Should().BeOfType<TextContent>().Which.Text.Should().Contain("too large");
    }

    [Fact]
    public void BadBase64_BecomesNote()
    {
        var parts = AskAttachmentBuilder.Build(
            new[] { new AskAttachment("x.png", "image/png", "not-base64!!") }, Limits);

        parts.Should().ContainSingle();
        parts[0].Should().BeOfType<TextContent>().Which.Text.Should().Contain("could not be decoded");
    }

    [Fact]
    public void UnsupportedType_BecomesNote()
    {
        var parts = AskAttachmentBuilder.Build(
            new[] { Att("archive.zip", "application/zip", new byte[] { 1, 2, 3 }) }, Limits);

        parts[0].Should().BeOfType<TextContent>().Which.Text.Should().Contain("unsupported");
    }

    [Fact]
    public void NullOrEmpty_ReturnsEmpty()
    {
        AskAttachmentBuilder.Build(null, Limits).Should().BeEmpty();
        AskAttachmentBuilder.Build(Array.Empty<AskAttachment>(), Limits).Should().BeEmpty();
    }

    // Author a minimal real .docx so ExtractDocxText is exercised end-to-end.
    private static byte[] MakeDocx(string text)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text(text)))));
            main.Document.Save();
        }
        return ms.ToArray();
    }
}
