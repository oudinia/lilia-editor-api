using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// An environment inside inline maths stays in its sentence (the launch bot's #51, 10 Nov 2026): the importer's
/// catch-all took `$A_1 = \begin{pmatrix}…\end{pmatrix}$` apart into a paragraph ending in "$", a raw block and a
/// paragraph starting with "$". Typst printed it; LaTeX, and so the accessible PDF, refused ("Missing $ inserted").
/// </summary>
public class LatexImportEnvInsideInlineMathTests
{
    private static async Task<List<ImportElement>> Import(string body) =>
        (await new LatexParser().ParseTextAsync("\\documentclass{article}\n\\begin{document}\n" + body + "\n\\end{document}\n")).Elements;

    [Fact]
    public async Task A_matrix_inside_inline_maths_stays_in_its_paragraph()
    {
        var elements = await Import(
            "For the matrix $A_1 = \\begin{pmatrix} 1 & 1 \\\\ 0 & 2 \\end{pmatrix}$ the eigenvalues are $\\lambda_1 = 1$ and $\\lambda_2 = 2$.");

        elements.OfType<ImportLatexPassthrough>().Should().BeEmpty();
        var paragraph = elements.OfType<ImportParagraph>().Single();
        paragraph.Text.Should().Contain("\\begin{pmatrix}").And.Contain("\\end{pmatrix}").And.Contain("the eigenvalues");
        paragraph.Text.Count(c => c == '$').Should().Be(6, "three complete inline maths spans");
    }

    [Fact]
    public async Task The_same_inside_parentheses_maths_and_a_real_environment_after_it_is_still_a_block()
    {
        var elements = await Import(
            "We have \\(B = \\begin{bmatrix} 2 & 0 \\\\ 0 & 3 \\end{bmatrix}\\) here.\n\n\\begin{tcolorbox}Boxed note.\\end{tcolorbox}");

        elements.OfType<ImportParagraph>().First().Text.Should().Contain("\\begin{bmatrix}").And.Contain("here.");
        elements.OfType<ImportLatexPassthrough>().Should().ContainSingle(p => p.LatexCode.Contains("tcolorbox"));
    }
}
