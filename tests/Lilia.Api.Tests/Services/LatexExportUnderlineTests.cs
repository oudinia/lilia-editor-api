using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The editor's underline mark (<c>__text__</c>) exports as <c>\underline</c>.
///
/// <para>The exporter had no rule for it, so the italic <c>_text_</c> rule took
/// the inner pair and "__underlined__" printed as "_underlined_" in italics
/// between two literal underscores. Found by the LaTeX feature-coverage e2e
/// (text.underline).</para>
/// </summary>
public class LatexExportUnderlineTests
{
    [Fact]
    public void Double_underscores_are_an_underline()
    {
        LaTeXExportService.FormatInlineContent("This is __underlined__ text.")
            .Should().Be(@"This is \underline{underlined} text.");
    }

    [Fact]
    public void Single_underscore_emphasis_and_snake_case_are_unchanged()
    {
        LaTeXExportService.FormatInlineContent("an _emphasis_ and value_of_x")
            .Should().Be(@"an \textit{emphasis} and value\_of\_x");
    }
}
