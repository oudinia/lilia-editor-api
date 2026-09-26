using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Typst.TypstHarness;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The equation mode the editor saves is the one the PDF prints.
///
/// <para>Measured 26 Sep: the editor writes <c>equationMode</c> ("display*",
/// "align*", "gather", "inline" …) and both exporters read only the older
/// <c>mode</c>. A starred equation — no number on screen — printed (1) from
/// either engine, every later number shifted, and Typst turned
/// <c>a &amp;= b \\ c &amp;= d</c> into "a, = b; c, = d".</para>
/// </summary>
public class EquationModeExportTests
{
    private static int _order;

    private static Block Eq(object content) => new()
    {
        Id = Guid.NewGuid(),
        Type = BlockTypes.Equation,
        SortOrder = _order++,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    private static string Latex(params Block[] blocks) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(
                new Document { Id = Guid.NewGuid(), Title = "Eq", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11 },
                blocks.ToList(), [], new LaTeXExportOptions());

    private static string TypstPdf(params Block[] blocks)
    {
        var source = new TypstExportService().BuildTypstDocument(Doc(), blocks.ToList(), null);
        var (text, error) = CompileToText(source);
        text.Should().NotBeNull($"it must compile — typst said: {error}\n\n{source}");
        return Regex.Replace(text!, @"[ \t]+", " ");
    }

    [Theory]
    [InlineData("display*", @"\[")]
    [InlineData("align*", @"\begin{align*}")]
    [InlineData("gather*", @"\begin{gather*}")]
    [InlineData("align", @"\begin{align}")]
    [InlineData("gather", @"\begin{gather}")]
    [InlineData("display", @"\begin{equation}")]
    public void The_latex_export_uses_the_editors_mode(string mode, string opening)
    {
        var tex = Latex(Eq(new { latex = "x = 1", equationMode = mode }));
        tex.Should().Contain(opening);
    }

    [Fact]
    public void An_inline_equation_is_inline_in_latex()
    {
        var tex = Latex(Eq(new { latex = "x = 1", equationMode = "inline" }));
        tex.Should().Contain("$x = 1$").And.NotContain(@"\begin{equation}");
    }

    [Fact]
    public void The_older_mode_field_still_works()
    {
        Latex(Eq(new { latex = "x = 1", mode = "gather" })).Should().Contain(@"\begin{gather}");
    }

    [Fact]
    public void A_starred_equation_takes_no_number_in_typst_and_the_next_one_is_1()
    {
        var text = TypstPdf(
            Eq(new { latex = "x = 1", equationMode = "display*" }),
            Eq(new { latex = @"a &= b \\ c &= d", equationMode = "align*" }),
            Eq(new { latex = "y = 2" }));

        text.Should().Contain("(1)");
        text.Should().NotContain("(2)").And.NotContain("(3)");
    }

    [Fact]
    public void An_alignment_keeps_its_rows_in_typst()
    {
        var text = TypstPdf(Eq(new { latex = @"a &= b \\ c &= d", equationMode = "align*" }));

        text.Should().NotContain(",").And.NotContain(";");
        text.Split('\n').Count(l => l.Contains('=')).Should().Be(2, "two rows, not one:\n" + text);
    }

    [Fact]
    public void A_matrix_still_separates_cells_and_rows()
    {
        var source = new TypstExportService().BuildTypstDocument(Doc(),
            [Eq(new { latex = @"\begin{pmatrix} a & b \\ c & d \end{pmatrix}" })], null);
        source.Should().MatchRegex(@"mat\(delim: ""\("", a ?, b ?; c ?, d\)");
    }

    [Fact]
    public void Cases_compile_with_one_row_per_case()
    {
        var text = TypstPdf(Eq(new { latex = @"f(x) = \begin{cases} 1 & x > 0 \\ 0 & \text{otherwise} \end{cases}" }));

        text.Should().Contain("otherwise");
        text.Should().NotContain(";");
        text.Split('\n').Count(l => l.Contains('1') || l.Contains("otherwise")).Should().BeGreaterThanOrEqualTo(2, text);
    }
}
