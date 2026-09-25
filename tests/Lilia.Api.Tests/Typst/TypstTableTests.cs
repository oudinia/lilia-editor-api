using System.Text.RegularExpressions;
using FluentAssertions;
using Lilia.Api.Services;
using Xunit;
using static Lilia.Api.Tests.Typst.TypstHarness;

namespace Lilia.Api.Tests.Typst;

/// <summary>
/// Tables in the Typst preview read as the LaTeX export writes them.
///
/// <para>Found on 25 Sep by exporting a real document through the default
/// preview and reading the PDF: a cell stored as an object printed as
/// <c>{"content": "1"}</c>, and the header row the editor keeps in
/// <c>headers</c> was missing. These compile real Typst and read the PDF.</para>
/// </summary>
public class TypstTableTests
{
    private static string Pdf(object tableContent)
    {
        var source = new TypstExportService().BuildTypstDocument(Doc(), [Block("table", tableContent)], null);
        var (text, error) = CompileToText(source);
        text.Should().NotBeNull($"the table must compile — typst said: {error}\n\n{source}");
        return Regex.Replace(text!, @"\s+", " ");
    }

    [Fact]
    public void Object_cells_print_their_text_not_json()
    {
        var text = Pdf(new
        {
            headers = new[] { new { content = "Model" }, new { content = "Top-1" } },
            rows = new object[] { new object[] { new { content = "ResNet" }, "76.1" } },
        });

        text.Should().Contain("ResNet").And.Contain("76.1");
        text.Should().NotContain("{").And.NotContain("content");
    }

    [Fact]
    public void The_headers_array_is_the_header_row()
    {
        var text = Pdf(new
        {
            headers = new[] { "Model", "Top-1" },
            rows = new[] { new[] { "ResNet", "76.1" } },
        });

        text.Should().MatchRegex(@"Model\s+Top-1.*ResNet\s+76\.1");
    }

    [Fact]
    public void The_header_row_is_bold_as_in_the_latex_export()
    {
        var source = new TypstExportService().BuildTypstDocument(Doc(),
            [Block("table", new { headers = new[] { "Model" }, rows = new[] { new[] { "a" } } })], null);
        source.Should().Contain("table.header([#strong[Model]])");
    }

    [Fact]
    public void A_colspan_spans_and_the_absorbed_cell_is_not_emitted()
    {
        // Full-width rows: the absorbed cell "x" is still in the grid.
        var text = Pdf(new
        {
            headers = new[] { "A", "B", "C" },
            rows = new object[]
            {
                new object[] { new { content = "wide", colspan = 2 }, "x", "c1" },
                new object[] { "a2", "b2", "c2" },
            },
        });

        text.Should().NotContain(" x ");
        text.Should().MatchRegex(@"wide\s+c1.*a2\s+b2\s+c2");
    }

    [Fact]
    public void A_rowspan_holds_its_column_and_the_next_row_stays_aligned()
    {
        var source = new TypstExportService().BuildTypstDocument(Doc(), [Block("table", new
        {
            headers = new[] { "A", "B" },
            rows = new object[]
            {
                new object[] { new { content = "tall", rowspan = 2 }, "b1" },
                new object[] { "absorbed", "b2" },
            },
        })], null);

        source.Should().Contain("table.cell(rowspan: 2)[tall]");
        source.Should().NotContain("absorbed");
        var (text, error) = CompileToText(source);
        text.Should().NotBeNull(error);
        Regex.Replace(text!, @"\s+", " ").Should().Contain("b2");
    }

    [Fact]
    public void A_short_row_is_padded_so_the_next_row_does_not_slide_up()
    {
        var source = new TypstExportService().BuildTypstDocument(Doc(), [Block("table", new
        {
            headers = new[] { "A", "B", "C" },
            rows = new object[] { new object[] { "a1" }, new object[] { "a2", "b2", "c2" } },
        })], null);

        source.Should().Contain("[a1], [], []");
    }

    [Fact]
    public void Imported_tables_without_headers_keep_their_first_row_as_the_header()
    {
        var source = new TypstExportService().BuildTypstDocument(Doc(),
            [Block("table", new { hasHeader = true, rows = new[] { new[] { "A", "B" }, new[] { "1", "2" } } })], null);
        source.Should().Contain("table.header([A], [B])");
    }
}
