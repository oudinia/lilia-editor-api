using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// launch-bot #221 retest (22 Nov 2026): an Overleaf project's \title{Overleaf zip test \#221} printed "\#221": the title
/// kept LaTeX's escape from the source, and the title writer escaped its backslash again (\textbackslash{}\#221).
/// </summary>
public class TitleEscapedCharacterTests
{
    [Theory]
    [InlineData(@"Overleaf zip test \#221", @"Overleaf zip test \#221")]
    [InlineData(@"50\% faster \& cheaper", @"50\% faster \& cheaper")]
    [InlineData(@"The \_underscore and \{braces\}", @"The \_underscore and \{braces\}")]
    [InlineData("A plain # and %", @"A plain \# and \%")]
    public void An_escaped_character_stays_escaped_once_and_a_bare_one_is_escaped(string title, string expected)
    {
        RenderService.FormatTitleMetaLatex(title).Should().Be(expected);
        LaTeXExportService.FormatTitleMetaLatex(title).Should().Be(expected);
    }

    [Fact]
    public async Task An_imported_title_with_an_escaped_hash_prints_it_once()
    {
        var doc = await new LatexParser().ParseTextAsync(@"\documentclass{article}
\title{Overleaf zip test \#221}
\begin{document}
\maketitle
Text.
\end{document}");
        RenderService.FormatTitleMetaLatex(doc.Title!).Should().EndWith(@"\#221").And.NotContain("textbackslash");
    }
}
