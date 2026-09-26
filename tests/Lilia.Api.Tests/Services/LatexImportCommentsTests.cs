using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// <c>%</c> comments are not imported as text.
///
/// <para>Nothing removed them: "Visible text. % a hidden comment" imported as
/// paragraph text and exported as a printed "\% a hidden comment". Found by
/// the LaTeX feature-coverage e2e (text.comments).</para>
/// </summary>
public class LatexImportCommentsTests
{
    private static async Task<ImportDocument> Parse(string body) =>
        await new LatexParser().ParseTextAsync(
            "\\documentclass{article}\n% preamble note\n\\begin{document}\n" + body + "\n\\end{document}\n");

    [Fact]
    public async Task A_trailing_comment_is_dropped()
    {
        var doc = await Parse("Visible text. % a hidden comment\nMore visible text.");

        var text = doc.Elements.OfType<ImportParagraph>().Single().Text;
        text.Should().NotContain("hidden comment");
        text.Should().Contain("Visible text.").And.Contain("More visible text.");
    }

    [Fact]
    public async Task A_comment_line_does_not_split_a_paragraph()
    {
        var doc = await Parse("First half\n% between the lines\nsecond half.");

        doc.Elements.OfType<ImportParagraph>().Should().ContainSingle()
            .Which.Text.Should().NotContain("between");
    }

    [Fact]
    public async Task Escaped_percent_and_verbatim_percent_survive()
    {
        var doc = await Parse("Costs 5\\% more.\n\n\\begin{verbatim}\nx = 100% % literal\n\\end{verbatim}");

        doc.Elements.OfType<ImportParagraph>().Single().Text.Should().Be("Costs 5% more.");
        doc.Elements.OfType<ImportCodeBlock>().Single().Text.Should().Be("x = 100% % literal");
    }

    [Theory]
    [InlineData("a\\\\% comment after a line break", "a")]
    [InlineData("see \\url{https://example.org/a%20b} now", "https://example.org/a%20b")]
    public void Strip_comments_reads_backslash_runs_and_keeps_url_arguments(string input, string kept)
    {
        var stripped = LatexParser.StripComments(input);
        stripped.Should().Contain(kept);
        if (input.Contains("comment after")) stripped.Should().NotContain("comment after");
    }
}
