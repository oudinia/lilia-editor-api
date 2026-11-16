using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// A sectioning command with an empty title (16 Nov 2026: the import sweep's hipster CV, D-015, starts its layout
/// with <c>\subsection*{}</c>) stayed in the text as a bare <c>\subsection*</c>: the heading pattern needs a title.
/// It makes no heading and leaves nothing behind; a real heading next to it is unchanged.
/// </summary>
public class EmptySectionTitleImportTests
{
    [Theory]
    [InlineData(@"\subsection*{}")]
    [InlineData(@"\section{ }")]
    [InlineData(@"\subsection*[]{}")]
    [InlineData(@"\paragraph{}")]
    public async Task An_empty_title_makes_no_heading_and_leaves_no_text(string empty)
    {
        var doc = await new LatexParser().ParseTextAsync($@"\documentclass{{article}}
\begin{{document}}
\section{{Real heading}}
{empty}
\vspace{{4em}}
Some text after it.
\end{{document}}");

        doc.Elements.OfType<ImportHeading>().Should().ContainSingle().Which.Text.Should().Be("Real heading");
        doc.Elements.OfType<ImportParagraph>().Should().NotContain(p => p.Text.Contains("section") || p.Text.Contains("paragraph"));
        doc.Elements.OfType<ImportParagraph>().Should().Contain(p => p.Text.Contains("Some text after it."));
    }
}
