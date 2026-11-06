using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Blocks;
using Lilia.Engines;

namespace Lilia.Api.Tests.Engines.Maths;

/// <summary>
/// Matrices, cases and split stored as the equation's mode (the launch bot's issue 33, 6 Nov 2026).
///
/// <para>The maths editor saves a [ ] matrix as <c>equationMode: "bmatrix"</c> with only its rows,
/// <c>1 &amp; 2 \\ 3 &amp; 4</c>; its preview adds the environment. The exports read the rows alone,
/// took their <c>&amp;</c> for an alignment, and printed the matrix as two numbered <c>align</c> rows
/// without brackets. Cases lost their brace the same way.</para>
/// </summary>
public class EquationWrappingModeTests
{
    private static JsonElement Content(string source, string mode, bool? numbered = null, string? label = null) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["source"] = source, ["equationMode"] = mode, ["numbered"] = numbered, ["label"] = label,
        }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value));

    [Theory]
    [InlineData("bmatrix")]
    [InlineData("pmatrix")]
    [InlineData("vmatrix")]
    [InlineData("Vmatrix")]
    public void A_matrix_mode_exports_as_one_equation_holding_that_matrix(string mode)
    {
        var latex = EquationLatex.Render(Content(@"1 & 2 \\ 3 & 4", mode));

        latex.Should().Be("\\begin{equation}\n\\begin{" + mode + @"}1 & 2 \\ 3 & 4\end{" + mode + "}\n\\end{equation}");
        latex.Should().NotContain("align");
    }

    [Fact]
    public void Cases_keep_their_brace_and_take_one_number()
    {
        var latex = EquationLatex.Render(Content(@"x^{2} & x \geq0 \\ -x & x<0", "cases", label: "abs"));

        latex.Should().Contain(@"\begin{cases}x^{2} & x \geq0 \\ -x & x<0\end{cases}");
        latex.Should().StartWith(@"\begin{equation}\label{");
        latex.Should().NotContain("align");
    }

    [Fact]
    public void Split_sits_inside_its_equation()
    {
        EquationLatex.Render(Content(@"a &= b + c \\ &= d", "split"))
            .Should().Be("\\begin{equation}\n\\begin{split}a &= b + c \\\\ &= d\\end{split}\n\\end{equation}");
    }

    [Fact]
    public void A_starred_or_unnumbered_matrix_has_no_number()
    {
        EquationLatex.Render(Content(@"1 & 0 \\ 0 & 1", "bmatrix*")).Should().StartWith(@"\begin{equation*}");
        EquationLatex.Render(Content(@"1 & 0 \\ 0 & 1", "bmatrix", numbered: false)).Should().StartWith(@"\begin{equation*}");
    }

    [Fact]
    public void A_source_that_already_opens_the_environment_is_not_wrapped_twice()
    {
        EquationContent.ReadMathSource(Content(@"\begin{bmatrix}1 & 2\end{bmatrix}", "bmatrix"))
            .Should().Be(@"\begin{bmatrix}1 & 2\end{bmatrix}");
    }

    [Fact]
    public void The_mode_reads_as_a_numbered_display()
    {
        EquationContent.ReadMode(Content(@"1 & 2", "bmatrix")).Should().Be(("display", true));
        EquationContent.ReadMode(Content(@"1 & 2", "cases*")).Should().Be(("display", false));
    }

    [Fact]
    public void Other_modes_and_the_verbatim_source_are_unchanged()
    {
        var align = Content(@"a &= b \\ c &= d", "align");
        EquationContent.ReadMathSource(align).Should().Be(@"a &= b \\ c &= d");
        EquationContent.ReadMode(align).Should().Be(("align", true));
        EquationContent.ReadSource(Content(@"1 & 2", "bmatrix")).Should().Be(@"1 & 2");
    }

    [Fact]
    public void The_typst_pdf_gets_a_bracketed_matrix_not_bare_rows()
    {
        var typst = LatexToTypst.Convert(EquationContent.ReadMathSource(Content(@"1 & 2 \\ 3 & 4", "bmatrix")));

        typst.Should().Contain("mat(").And.Contain("\"[\"");
    }
}
