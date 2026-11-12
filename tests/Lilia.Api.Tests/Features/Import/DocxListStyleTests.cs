using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// Launch-bot #76 (12 Nov 2026): the bot's Word file (python-docx's default template) lost its
/// numbered list (numbered only through the "List Number" style), its bold, and its table headers
/// ("Column 1"). The fixture is that exact file.
/// </summary>
public class DocxListStyleTests
{
    private static async Task<ImportDocument> Parse()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "docx-corpus", "curated", "python-docx-list-number-style.docx");
        return await new DocxParser().ParseAsync(path);
    }

    [Fact]
    public async Task A_list_numbered_through_its_style_is_a_numbered_list()
    {
        var doc = await Parse();
        var items = doc.Elements.OfType<ImportListItem>().ToList();
        items.Select(i => i.Text).Should().Equal("Read the meter value.", "Apply the base tariff.", "Add the network fee.");
        items.Should().OnlyContain(i => i.IsNumbered);
    }

    [Fact]
    public async Task Headings_stay_headings()
    {
        var doc = await Parse();
        doc.Elements.OfType<ImportHeading>().Select(h => (h.Text, h.Level))
            .Should().Equal(("Pricing model overview", 1), ("Steps", 2));
    }

    [Fact]
    public async Task Direct_bold_is_kept()
    {
        var doc = await Parse();
        var para = doc.Elements.OfType<ImportParagraph>().First(p => p.Text.Contains("base tariff"));
        var at = para.Text.IndexOf("base tariff", StringComparison.Ordinal);
        para.Formatting.Should().Contain(f => f.Type == FormattingType.Bold && f.Start == at && f.Length == "base tariff".Length);
    }

    [Fact]
    public async Task A_table_styled_with_a_first_row_has_its_own_headers()
    {
        var doc = await Parse();
        doc.Elements.OfType<ImportTable>().Single().HasHeaderRow.Should().BeTrue();
    }
}
