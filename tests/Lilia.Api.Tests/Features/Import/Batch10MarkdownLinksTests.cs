using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The launch bot's Markdown test (18 Nov 2026, #198–#201): the Word export had no links, Markdown alignment was
/// ignored, a Markdown title printed today's date, and task lists, mail links and README badges came out wrong.
/// </summary>
public class Batch10MarkdownLinksTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static async Task<byte[]> Word(params ExportBlock[] blocks)
    {
        var doc = new ExportDocument { Title = "T", Blocks = [.. blocks] };
        DocumentExportService.ApplyLanguage(doc, new Document { Title = "T", Language = "en" });
        return await new DocxExportService().ExportAsync(doc);
    }

    private static ExportBlock Paragraph(string text) => new()
    {
        Type = "paragraph",
        Content = new ExportBlockContent { Text = text, RichText = DocumentExportService.ParseInlineFormatting(text, null) },
    };

    private static XDocument Part(byte[] docx, string name)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return XDocument.Parse(reader.ReadToEnd());
    }

    private static List<string> SchemaErrors(byte[] docx)
    {
        using var word = WordprocessingDocument.Open(new MemoryStream(docx), false);
        return new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(word)
            .Where(e => e.Path?.XPath?.Contains("/w:tblPr[") != true)
            .Select(e => $"{e.Path?.XPath}: {e.Description}").ToList();
    }

    // #198: every \href and \url became plain text in Word.
    [Fact]
    public async Task Links_are_word_hyperlinks()
    {
        var bytes = await Word(Paragraph(@"See the \href{https://arxiv.org/abs/1712.01887}{**paper**} or visit \url{https://example.org}."));
        var body = Part(bytes, "word/document.xml").Descendants(W + "body").Single();
        var links = body.Descendants(W + "hyperlink").ToList();
        links.Select(l => string.Concat(l.Descendants(W + "t").Select(t => t.Value))).Should().Equal("paper", "https://example.org");
        links[0].Descendants(W + "b").Should().NotBeEmpty("the link's bold is kept");

        using var word = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        word.MainDocumentPart!.HyperlinkRelationships.Select(r => r.Uri.OriginalString)
            .Should().BeEquivalentTo("https://arxiv.org/abs/1712.01887", "https://example.org");
        body.Elements(W + "p").Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value)))
            .Should().Contain("See the paper or visit https://example.org.");
        SchemaErrors(bytes).Should().BeEmpty();
    }

    // #199: centre and right columns, as chosen in the Markdown, in Word too.
    [Fact]
    public async Task Word_cells_follow_their_columns_alignment()
    {
        var table = new ExportBlock
        {
            Type = "table",
            Content = new ExportBlockContent
            {
                Rows = [[new ExportTableCell { Text = "a" }, new ExportTableCell { Text = "x", Align = "center" }, new ExportTableCell { Text = "1", Align = "right" }]],
            },
        };
        var body = Part(await Word(table), "word/document.xml").Descendants(W + "body").Single();
        var jc = body.Descendants(W + "tc").Select(tc => tc.Descendants(W + "jc").FirstOrDefault()?.Attribute(W + "val")?.Value).ToList();
        jc.Should().Equal(null, "center", "right");
    }

    [Fact]
    public void Markdown_table_alignment_is_kept()
    {
        var table = MarkdownTextParser.Parse("| Left | Right | Centre |\n|:-----|------:|:------:|\n| a | 1.5 | x |\n| bb | 22.25 | yy |\n")
            .OfType<ImportTable>().Single();
        table.ColumnAlign.Should().Equal("l", "r", "c");
        var content = ImportTableLayout.WithLayout(table, []);
        ((string[])content["columnAlign"]).Should().Equal("l", "r", "c");
    }

    // #200: a Markdown title without a date printed today's date.
    [Fact]
    public void A_markdown_title_without_a_date_has_none()
    {
        PastedText.NoDateIsNone(new Dictionary<string, object> { ["title"] = "T", ["date"] = "" })
            .Should().ContainKey("dateMode").WhoseValue.Should().Be("none");
        PastedText.NoDateIsNone(new Dictionary<string, object> { ["title"] = "T", ["date"] = "March 2026" })
            .Should().NotContainKey("dateMode");
    }

    // #201: task boxes, mail autolinks and a linked badge.
    [Fact]
    public void Task_items_mail_links_and_badges()
    {
        var items = MarkdownTextParser.Parse("- [x] done item\n- [ ] open item\n").OfType<ImportListItem>().ToList();
        items.Select(i => i.Text).Should().Equal(@"$\boxtimes$ done item", @"$\square$ open item");

        MarkdownTextParser.Inline("Mail <maintainers@example.org> or visit <https://example.org>.")
            .Should().Be(@"Mail \href{mailto:maintainers@example.org}{maintainers@example.org} or visit \url{https://example.org}.");

        MarkdownTextParser.Inline("[![CI](https://img.shields.io/badge/ci-passing-green.svg)](https://github.com/example/fastgrad/actions)")
            .Should().Be(@"\href{https://github.com/example/fastgrad/actions}{CI}");
    }
}
