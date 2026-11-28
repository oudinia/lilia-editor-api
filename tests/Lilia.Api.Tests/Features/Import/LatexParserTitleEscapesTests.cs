using FluentAssertions;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// An imported title is plain text in the editor (launch-bot #221 retest, 28 Nov 2026): a character escaped for LaTeX in
/// \title is the character itself, so the title block no longer shows "\#221". The exports escape it again.
/// </summary>
public class LatexParserTitleEscapesTests
{
    private readonly LatexParser _parser = new();

    private Task<string?> TitleOf(string title) => _parser.ParseTextAsync($$"""
        \documentclass{article}
        \title{{{title}}}
        \author{Ann Author}
        \begin{document}
        \maketitle
        Body.
        \end{document}
        """).ContinueWith(t => t.Result.Title);

    [Theory]
    [InlineData(@"Notes \#221", "Notes #221")]
    [InlineData(@"Profit \& Loss: 50\% of \$10", "Profit & Loss: 50% of $10")]
    [InlineData(@"The \_tmp file and \{braces\}", "The _tmp file and {braces}")]
    [InlineData("A plain title", "A plain title")]
    public async Task A_title_s_escaped_characters_become_the_characters(string latex, string expected)
    {
        (await TitleOf(latex)).Should().Be(expected);
    }

    [Fact]
    public void A_line_break_is_not_mistaken_for_an_escape()
    {
        LatexParser.UnescapeLatexSpecials(@"First line\\#2").Should().Be(@"First line\\#2");
    }
}
