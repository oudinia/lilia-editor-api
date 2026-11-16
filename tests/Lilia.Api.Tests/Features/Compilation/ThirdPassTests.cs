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

    // The launch bot's #118 (16 Nov 2026): revtex4-2 sizes its contents' number boxes from widths the pass before
    // measured, and asks for no rerun. Two passes printed "IIntroduction"; the third lays it out.
    [Fact]
    public async Task A_revtex_contents_has_room_for_its_numbers()
    {
        const string tex = @"\documentclass[aip,preprint]{revtex4-2}
\begin{document}
\tableofcontents
\section{Introduction}
Text.
\section{Methods}
\subsection{Setup}
Text.
\end{document}";
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, "pdflatex", timeout: 120);
        var text = await Tool("pdftotext", pdf, "-");
        text.Should().Contain("I. Introduction").And.Contain("II. Methods");
    }

    [Fact]
    public void Only_a_change_in_revtexs_widths_asks_for_another_pass()
    {
        BibliographyPasses.TocWidthsMoved("\\relax\n", "\\relax\n\\gdef \\tocmax@section{18.68pt}\n").Should().BeTrue();
        BibliographyPasses.TocWidthsMoved("\\gdef \\tocmax@section{18.68pt}\n", "\\gdef \\tocmax@section{18.68pt}\n").Should().BeFalse();
        BibliographyPasses.TocWidthsMoved("\\relax\n\\newlabel{a}{{1}{1}}\n", "\\relax\n\\newlabel{a}{{1}{2}}\n").Should().BeFalse("other .aux changes are LaTeX's rerun check's");
    }
}
