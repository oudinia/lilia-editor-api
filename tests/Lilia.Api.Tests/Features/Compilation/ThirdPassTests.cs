using System.Text;
using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Engines.Themes.DocumentThemeCompileTests;
using Lilia.Api.Tests.Engines.Themes;

namespace Lilia.Api.Tests.Features.Compilation;

/// <summary>
/// A third LaTeX pass when the second says labels may have changed (2 Nov 2026). The contents appears
/// in pass two and moves every page after it, so what pass two printed from pass one's labels is a page
/// off. veraPDF found the accessible PDF's tags stale the same way; here, a \pageref shows it in the text.
/// </summary>
public class ThirdPassTests
{
    [Fact]
    public async Task A_reference_after_a_contents_names_the_page_it_is_on()
    {
        var sb = new StringBuilder(@"\documentclass{article}
\begin{document}
\tableofcontents
");
        // Enough sections for the contents to fill a page, so the target moves on in pass two.
        for (var i = 1; i <= 60; i++) sb.AppendLine($@"\section{{Section {i}}}");
        sb.AppendLine(@"\newpage
\section{Target}\label{target}
The target is on page \pageref{target}.
\end{document}");

        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(sb.ToString(), "pdflatex", timeout: 120);

        // pdftotext separates pages with a form feed: the page the sentence is on, counted from 1.
        var pages = (await Tool("pdftotext", pdf, "-")).Split('\f');
        var page = Array.FindIndex(pages, p => p.Contains("The target is on page")) + 1;
        page.Should().BeGreaterThan(1);
        pages[page - 1].Should().Contain($"The target is on page {page}.");
    }
}
