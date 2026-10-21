using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Themes.DocumentThemeCompileTests;

namespace Lilia.Api.Tests.Themes;

/// <summary>
/// A table's own alternating row colours (21 Oct 2026: Ask Lilia couldn't colour rows, "the table
/// schema has no field"). rowColors: { odd, even } in hex; odd is the first body row.
/// </summary>
public class TableRowColoursTests
{
    private static JsonElement Content(object rowColors) => JsonSerializer.SerializeToElement(new
    {
        caption = "Accuracy by model",
        headers = new[] { "Model", "Accuracy" },
        rows = Enumerable.Range(1, 5).Select(i => new[] { $"Model{i}", $"{70 + i}.5" }).ToArray(),
        rowColors,
    });

    [Fact]
    public void Both_colours_start_on_the_first_body_row_and_keep_a_Banded_paper_off()
    {
        var latex = RenderService.RowColoursLatex(Content(new { odd = "#f2f2f2", even = "E8F0FE" }))!;
        latex.Should().Contain(LatexText.FewRows, "a Banded paper must not stripe over them");
        latex.Should().Contain(@"\definecolor{liliaRowOdd}{HTML}{F2F2F2}").And.Contain(@"\definecolor{liliaRowEven}{HTML}{E8F0FE}");
        // Row 1 is the header: from row 2 the "even" slot falls on the first body row.
        latex.Should().EndWith(@"\rowcolors{2}{liliaRowEven}{liliaRowOdd}");
    }

    [Fact]
    public void One_colour_leaves_the_other_rows_white_and_nonsense_is_ignored()
    {
        RenderService.RowColoursLatex(Content(new { odd = "#E6F4EA" }))!.Should().EndWith(@"\rowcolors{2}{}{liliaRowOdd}");
        RenderService.RowColoursLatex(Content(new { odd = "green}\\input{/etc/passwd", even = "#12" })).Should().BeNull();
        RenderService.RowColoursLatex(JsonSerializer.SerializeToElement(new { rows = Array.Empty<string[]>() })).Should().BeNull();
    }

    [Fact]
    public void Typst_fills_body_rows_only()
    {
        var fill = TypstRenderService.TypstRowFill(Content(new { odd = "#F2F2F2", even = "#E8F0FE" }), hasHeaders: true)!;
        fill.Should().Be("(x, y) => if y < 1 { none } else if calc.even(y - 1) { rgb(\"#F2F2F2\") } else { rgb(\"#E8F0FE\") }");
        TypstRenderService.TypstRowFill(Content(new { odd = "nope" }), true).Should().BeNull();
    }

    [Fact]
    public void A_follow_up_carries_the_conversation_so_far()
    {
        var text = AiController.WithHistory("what would make it possible ?",
            [new ReviseTurn("apply alternate row colours", "Tables have no field for that.")]);
        text.Should().Contain("The author asked: apply alternate row colours")
            .And.Contain("You answered: Tables have no field for that.")
            .And.Contain("Now the author says: what would make it possible ?");
        AiController.WithHistory("bold the header", null).Should().Be("bold the header");
    }

    [Trait("Category", "RequiresTeX")]
    [Theory]
    [InlineData("classic")]
    [InlineData("cerulean")]
    public async Task The_PDF_prints_both_colours_even_on_a_Banded_paper(string theme)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "Rows", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
            LatexDocumentClass = "article",
            Look = JsonSerializer.Serialize(new { theme, paper = "theme", pins = new { }, tables = new { style = "banded" } }),
        };
        var table = new Block { Id = Guid.NewGuid(), Type = "table", SortOrder = 0, Content = JsonDocument.Parse(Content(new { odd = "#F2F2F2", even = "#E8F0FE" }).GetRawText()) };
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, [table], [], new LaTeXExportOptions());
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, "pdflatex", timeout: 180);

        var colours = FillColours(pdf);
        colours.Should().Contain(c => Near(c, "#F2F2F2"), "the odd rows' light gray");
        colours.Should().Contain(c => Near(c, "#E8F0FE"), "the even rows' light blue");
    }
}
