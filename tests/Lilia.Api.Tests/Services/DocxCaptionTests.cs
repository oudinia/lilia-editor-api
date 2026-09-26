using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Captions in the Word export are Word captions: the Caption style, "Table " +
/// a SEQ field + ": " + the text — so Word numbers them and its cross-reference
/// tool finds them. Found by the e2e export spec (26 Sep): the .docx had no
/// table caption at all, while every other format printed it.
/// </summary>
public class DocxCaptionTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static XDocument DocumentXml(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        return XDocument.Parse(reader.ReadToEnd());
    }

    private static string Text(XElement e) => string.Concat(e.Descendants(W + "t").Select(t => t.Value));

    private static ExportBlock Table(string? caption) => new()
    {
        Type = "table",
        Content = new ExportBlockContent
        {
            Caption = caption,
            Rows = [[new ExportTableCell { Text = "ResNet" }, new ExportTableCell { Text = "76.1" }]],
        },
    };

    private static ExportBlock Figure(string caption) => new()
    {
        Type = "figure",
        Content = new ExportBlockContent { Caption = caption },
    };

    private static async Task<XElement> Body(params ExportBlock[] blocks) =>
        DocumentXml(await new DocxExportService().ExportAsync(new ExportDocument { Title = "T", Blocks = [.. blocks] }))
            .Descendants(W + "body").Single();

    [Fact]
    public async Task A_table_caption_sits_above_the_table_as_a_numbered_word_caption()
    {
        var body = await Body(Table("Top-1 accuracy"));
        var children = body.Elements().ToList();
        var tableAt = children.FindIndex(e => e.Name == W + "tbl");
        var caption = children[tableAt - 1];

        caption.Descendants(W + "pStyle").Single().Attribute(W + "val")!.Value.Should().Be("Caption");
        Text(caption).Should().Be("Table 1: Top-1 accuracy");
        caption.Descendants(W + "fldSimple").Single().Attribute(W + "instr")!.Value.Should().Be(@" SEQ Table \* ARABIC ");
    }

    [Fact]
    public async Task Only_captioned_tables_take_a_number_as_in_latex()
    {
        var body = await Body(Table("First"), Table(null), Table("Second"));
        var captions = body.Elements(W + "p")
            .Where(p => p.Descendants(W + "pStyle").Any(s => s.Attribute(W + "val")!.Value == "Caption"))
            .Select(Text).ToList();
        captions.Should().Equal("Table 1: First", "Table 2: Second");
    }

    [Fact]
    public async Task Figures_and_tables_are_counted_separately()
    {
        var body = await Body(Figure("Architecture"), Table("Results"), Figure("Loss curve"));
        var captions = body.Descendants(W + "p")
            .Where(p => p.Descendants(W + "pStyle").Any(s => s.Attribute(W + "val")!.Value == "Caption"))
            .Select(Text).ToList();
        captions.Should().Equal("Figure 1: Architecture", "Table 1: Results", "Figure 2: Loss curve");
    }

    [Fact]
    public async Task The_caption_style_is_defined_so_word_recognises_it()
    {
        var docx = await new DocxExportService().ExportAsync(new ExportDocument { Title = "T", Blocks = [Table("x")] });
        using var ms = new MemoryStream(docx);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("word/styles.xml")!.Open());
        XDocument.Parse(reader.ReadToEnd()).Descendants(W + "style")
            .Should().Contain(s => s.Attribute(W + "styleId")!.Value == "Caption");
    }
}
