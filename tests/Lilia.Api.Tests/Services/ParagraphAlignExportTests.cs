using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// A paragraph centred, set right or justified in the editor (Block menu, saved as
/// <c>align</c>, web #489) prints that way in every export, not only on the canvas.
/// </summary>
public class ParagraphAlignExportTests
{
    private readonly RenderService _latex = new(null!, new Mock<ILogger<RenderService>>().Object);
    private readonly TypstRenderService _typst = new(null!, new Mock<ILogger<TypstRenderService>>().Object);

    private static Block Para(string json) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = "paragraph", SortOrder = 0,
        Content = JsonDocument.Parse(json),
    };

    [Theory]
    [InlineData("center", "\\begin{center}\nCentred.\n\\end{center}")]
    [InlineData("right", "\\begin{flushright}\nCentred.\n\\end{flushright}")]
    public void LaTeX_puts_a_centred_or_right_paragraph_in_its_environment(string align, string expected)
    {
        _latex.RenderBlockToLatex(Para($$"""{"text":"Centred.","align":"{{align}}"}""")).Should().Contain(expected);
    }

    [Theory]
    [InlineData("""{"text":"Plain."}""")]
    [InlineData("""{"text":"Plain.","align":"justify"}""")]
    [InlineData("""{"text":"Plain.","align":"sideways"}""")]
    public void LaTeX_leaves_a_default_justified_or_unknown_alignment_as_the_class_sets_it(string json)
    {
        var tex = _latex.RenderBlockToLatex(Para(json));
        tex.Should().NotContain("\\begin{").And.Contain("Plain.");
    }

    [Fact]
    public void HTML_carries_the_alignment_as_a_style()
    {
        _latex.RenderBlockToHtml(Para("""{"text":"Right.","align":"right"}"""))
            .Should().Be("<p class=\"paragraph\" style=\"text-align:right\">Right.</p>");
        _latex.RenderBlockToHtml(Para("""{"text":"Plain."}""")).Should().Be("<p class=\"paragraph\">Plain.</p>");
    }

    [Theory]
    [InlineData("center", "#align(center)[Text.]")]
    [InlineData("right", "#align(right)[Text.]")]
    [InlineData("justify", "#par(justify: true)[Text.]")]
    public void Typst_aligns_the_paragraph(string align, string expected)
    {
        _typst.RenderBlockToTypst(Para($$"""{"text":"Text.","align":"{{align}}"}""")).Should().Contain(expected);
    }

    [Theory]
    [InlineData("center", "center")]
    [InlineData("right", "right")]
    [InlineData("justify", "both")]
    public async Task Word_sets_the_paragraph_justification(string align, string wordValue)
    {
        var doc = new ExportDocument
        {
            Title = "Aligned",
            Blocks = [new ExportBlock { Type = "paragraph", Content = new ExportBlockContent { Text = "Aligned.", Alignment = align } }],
        };
        var bytes = await new DocxExportService().ExportAsync(doc);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var para = XDocument.Parse(reader.ReadToEnd()).Descendants(w + "p")
            .First(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)) == "Aligned.");
        para.Element(w + "pPr")?.Element(w + "jc")?.Attribute(w + "val")?.Value.Should().Be(wordValue);
    }
}
