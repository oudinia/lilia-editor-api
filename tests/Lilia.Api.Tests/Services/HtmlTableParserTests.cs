using FluentAssertions;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Services;

public class HtmlTableParserTests
{
    [Fact]
    public void Parse_SimpleTable_ReturnsCorrectRows()
    {
        var html = "<table><tr><td>A</td><td>B</td></tr><tr><td>1</td><td>2</td></tr></table>";

        var (rows, hasHeader) = HtmlTableParser.Parse(html);

        rows.Should().HaveCount(2);
        rows[0].Should().HaveCount(2);
        rows[0][0].Text.Should().Be("A");
        rows[0][1].Text.Should().Be("B");
        rows[1][0].Text.Should().Be("1");
        rows[1][1].Text.Should().Be("2");
        hasHeader.Should().BeFalse();
    }

    [Fact]
    public void Parse_TableWithTh_DetectsHeaderRow()
    {
        var html = "<table><tr><th>Name</th><th>Value</th></tr><tr><td>x</td><td>1</td></tr></table>";

        var (rows, hasHeader) = HtmlTableParser.Parse(html);

        rows.Should().HaveCount(2);
        hasHeader.Should().BeTrue();
        rows[0][0].Text.Should().Be("Name");
        rows[0][1].Text.Should().Be("Value");
    }

    [Fact]
    public void Parse_TableWithThead_DetectsHeaderRow()
    {
        var html = "<table><thead><tr><td>H1</td><td>H2</td></tr></thead><tbody><tr><td>D1</td><td>D2</td></tr></tbody></table>";

        var (rows, hasHeader) = HtmlTableParser.Parse(html);

        rows.Should().HaveCount(2);
        hasHeader.Should().BeTrue();
    }

    [Fact]
    public void Parse_TableWithColspan_ParsesSpan()
    {
        var html = "<table><tr><td colspan=\"2\">Merged</td></tr><tr><td>A</td><td>B</td></tr></table>";

        var (rows, _) = HtmlTableParser.Parse(html);

        rows[0][0].ColSpan.Should().Be(2);
        rows[0][0].Text.Should().Be("Merged");
    }

    [Fact]
    public void Parse_TableWithRowspan_ParsesSpan()
    {
        var html = "<table><tr><td rowspan=\"3\">Tall</td><td>B</td></tr></table>";

        var (rows, _) = HtmlTableParser.Parse(html);

        rows[0][0].RowSpan.Should().Be(3);
    }

    [Fact]
    public void Parse_EmptyCells_ReturnsEmptyText()
    {
        var html = "<table><tr><td></td><td>Data</td></tr></table>";

        var (rows, _) = HtmlTableParser.Parse(html);

        rows[0][0].Text.Should().Be("");
        rows[0][1].Text.Should().Be("Data");
    }

    [Fact]
    public void Parse_EmptyHtml_ReturnsEmptyList()
    {
        var (rows, hasHeader) = HtmlTableParser.Parse("");

        rows.Should().BeEmpty();
        hasHeader.Should().BeFalse();
    }

    [Fact]
    public void Parse_Null_ReturnsEmptyList()
    {
        var (rows, hasHeader) = HtmlTableParser.Parse(null!);

        rows.Should().BeEmpty();
        hasHeader.Should().BeFalse();
    }

    [Fact]
    public void Parse_CellsWithInnerHtml_StripsTagsToPlainText()
    {
        var html = "<table><tr><td><b>Bold</b> and <i>italic</i></td></tr></table>";

        var (rows, _) = HtmlTableParser.Parse(html);

        rows[0][0].Text.Should().Be("Bold and italic");
    }

    [Fact]
    public void Parse_CellsWithHtmlEntities_DecodesEntities()
    {
        var html = "<table><tr><td>A &amp; B &lt; C</td></tr></table>";

        var (rows, _) = HtmlTableParser.Parse(html);

        rows[0][0].Text.Should().Be("A & B < C");
    }
}
