using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #183 (19 Nov 2026): in an imported thesis, \pagenumbering{roman}\thispagestyle{empty} printed "romanempty",
/// \pagenumbering{arabic} printed "arabic", and \addcontentsline{toc}{chapter}{Acknowledgements} printed
/// "toc{chapter}{Acknowledgements}" while the contents lost the entry. The body flattener kept their arguments as text.
/// Page numbering and styles now go with their argument; a contents line is kept as raw LaTeX, after its heading.
/// </summary>
public class Batch183PageCommandsTests
{
    private const string Thesis = @"\documentclass[12pt]{report}
\begin{document}
\pagenumbering{roman}\thispagestyle{empty}
Title page text.
\newpage
\chapter*{Acknowledgements}
\addcontentsline{toc}{chapter}{Acknowledgements}
Thanks.
\tableofcontents
\newpage
\pagenumbering{arabic}
\chapter{Introduction}
\end{document}";

    private static async Task<ImportDocument> Parse(string tex) => await new LatexParser().ParseTextAsync(tex);

    private static string AllText(ImportDocument doc) =>
        string.Join("\n", doc.Elements.OfType<ImportParagraph>().Select(p => p.Text));

    [Fact]
    public async Task Page_numbering_and_page_style_print_nothing_in_a_thesis()
    {
        var text = AllText(await Parse(Thesis));
        text.Should().Contain("Title page text.");
        text.Should().NotContain("roman").And.NotContain("empty").And.NotContain("arabic");
    }

    [Fact]
    public async Task A_contents_line_is_kept_as_raw_latex_and_not_printed()
    {
        var doc = await Parse(Thesis);
        AllText(doc).Should().NotContain("toc").And.NotContain("{chapter}");
        doc.Elements.OfType<ImportLatexPassthrough>().Select(p => p.LatexCode)
            .Should().Contain(@"\addcontentsline{toc}{chapter}{Acknowledgements}");
    }

    [Fact]
    public async Task The_contents_line_comes_right_after_its_unnumbered_chapter()
    {
        var elements = (await Parse(Thesis)).Elements.OrderBy(e => e.Order).ToList();
        var heading = elements.FindIndex(e => e is ImportHeading h && h.Text.Contains("Acknowledgements"));
        heading.Should().BeGreaterThanOrEqualTo(0);
        elements[heading + 1].Should().BeOfType<ImportLatexPassthrough>()
            .Which.LatexCode.Should().StartWith(@"\addcontentsline");
        elements.OfType<ImportParagraph>().Should().Contain(p => p.Text.Contains("Thanks."));
    }

    [Fact]
    public async Task A_page_style_after_maketitle_leaves_no_word_under_the_title()
    {
        var doc = await Parse(@"\documentclass{article}
\title{An Article}
\author{A. Author}
\begin{document}
\maketitle
\thispagestyle{empty}
\pagestyle{plain}
\section{Introduction}
Body text.
\end{document}");
        var text = AllText(doc);
        text.Should().Contain("Body text.");
        text.Should().NotContain("empty").And.NotContain("plain");
    }

    [Fact]
    public async Task A_contents_line_with_a_command_in_its_title_is_kept_whole()
    {
        var doc = await Parse(@"\documentclass{report}
\begin{document}
\chapter*{List of Appendices}
\addcontentsline{toc}{chapter}{List of \textsc{Appendices}}
Text.
\end{document}");
        doc.Elements.OfType<ImportLatexPassthrough>().Select(p => p.LatexCode)
            .Should().Contain(@"\addcontentsline{toc}{chapter}{List of \textsc{Appendices}}");
        AllText(doc).Should().NotContain("toc");
    }
}
