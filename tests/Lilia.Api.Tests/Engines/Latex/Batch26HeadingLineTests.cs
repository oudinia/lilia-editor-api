using FluentAssertions;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #145 (19 Nov 2026): REVTeX's sample writes a section title over two lines. The line break became a
/// blank line inside \section{…}, a paragraph break, and the compile stopped ("Runaway argument", \@sect@ltx).
/// </summary>
public class Batch26HeadingLineTests
{
    private static readonly RenderService Render = new(null!, NullLogger<RenderService>.Instance);

    [Fact]
    public void A_heading_written_over_two_lines_is_one_line()
    {
        var latex = Render.RenderBlockToLatex(B("heading", new { text = "First-level heading: The line\nbreak was forced via", level = 1 }));
        latex.Should().StartWith(@"\section{First-level heading: The line break was forced via}").And.NotContain("\n\n");
    }

    [Fact]
    public async Task The_revtex_sample_heading_imports_and_renders_on_one_line()
    {
        var doc = await new LatexParser().ParseTextAsync(@"\documentclass{revtex4-2}
\begin{document}
\section{\label{sec:level1}First-level heading:\protect\\ The line
break was forced \lowercase{via} \textbackslash\textbackslash}
Text.
\end{document}");
        var heading = doc.Elements.OfType<ImportHeading>().Single();
        var latex = Render.RenderBlockToLatex(B("heading", new { text = heading.Text, level = 1, label = heading.Label ?? "" }));
        latex.Should().NotContain("\n\n").And.Contain("The line break was forced");
    }
}
