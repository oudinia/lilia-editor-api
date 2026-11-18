using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Export.Docx;

/// <summary>
/// The launch bot's #110 (14 Nov 2026): the PDF of a document without a Title block still prints the document's name
/// and the date (\maketitle from \title{name} and \today), while Word printed neither. Word now prints them too.
/// </summary>
public class WordImpliedTitleTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static async Task<List<string>> Paragraphs(ExportDocument doc)
    {
        var bytes = await new DocxExportService().ExportAsync(doc);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var s = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(s).Descendants(W + "p")
            .Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value)))
            .Where(t => t.Length > 0).ToList();
    }

    private static ExportBlock Para(string text) => new() { Type = "paragraph", Content = new ExportBlockContent { Text = text } };

    [Fact]
    public async Task Without_a_Title_block_the_document_s_name_and_the_date_come_first()
    {
        var paras = await Paragraphs(new ExportDocument { Title = "CV W46 test", Blocks = [Para("Ada Lovelace, London")] });

        paras[0].Should().Be("CV W46 test");
        DateTime.TryParse(paras[1], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)
            .Should().BeTrue($"\"{paras[1]}\" is the date, as \\today prints it");
        date.Date.Should().BeCloseTo(DateTime.Now.Date, TimeSpan.FromDays(1));
        paras[2].Should().Be("Ada Lovelace, London");
    }

    [Fact]
    public async Task A_Title_block_is_the_title_and_nothing_is_added()
    {
        var title = new ExportBlock { Type = "title", Content = new ExportBlockContent { Text = "My paper", DateText = "Spring 2026" } };
        var paras = await Paragraphs(new ExportDocument { Title = "Untitled draft", Blocks = [title, Para("Body.")] });

        paras.Should().Equal("My paper", "Spring 2026", "Body.");
    }

    [Fact]
    public async Task A_first_heading_that_already_says_the_title_is_not_doubled()
    {
        var heading = new ExportBlock { Type = "heading", Content = new ExportBlockContent { Text = "On  Graphs", Level = 1 } };
        var paras = await Paragraphs(new ExportDocument { Title = "On Graphs", Blocks = [heading, Para("Body.")] });

        paras.Should().Equal("On  Graphs", "Body.");
    }

    [Fact]
    public async Task A_document_without_a_name_gets_no_title()
        => (await Paragraphs(new ExportDocument { Title = "  ", Blocks = [Para("Body.")] })).Should().Equal("Body.");

    // launch-bot #154 (18 Nov 2026): a title block with dateMode "none" (an imported \date{}) and a stored "\today"
    // printed today's date in Word; the PDF prints none.
    [Theory]
    [InlineData("""{"date":"\\today","dateMode":"none"}""", "")]
    [InlineData("""{"date":"","dateMode":"none"}""", "")]
    [InlineData("""{"date":"Spring 2026","dateMode":""}""", "Spring 2026")]
    [InlineData("""{"date":"Spring 2026"}""", "Spring 2026")]
    public void A_title_dated_none_prints_no_date_in_word(string json, string expected)
    {
        using var content = System.Text.Json.JsonDocument.Parse(json);
        Lilia.Api.Services.DocumentExportService.TitleDate(content.RootElement).Should().Be(expected);
    }
}
