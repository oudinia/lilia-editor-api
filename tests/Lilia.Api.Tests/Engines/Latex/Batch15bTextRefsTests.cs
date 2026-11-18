using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's Overleaf round 1 (18 Nov 2026): \verb shown as code (#222's follow-up), \onlinecite (#231), \Cref in
/// a caption (#233), a caption's own \label written twice (#234), and size switches in a title (#226).
/// </summary>
public class Batch15bTextRefsTests
{
    private static Task<ImportDocument> Parse(string body) => new LatexParser().ParseTextAsync(
        "\\documentclass{article}\n\\begin{document}\n" + body + "\n\\end{document}\n");

    [Fact]
    public async Task Verb_comes_back_as_a_code_span()
    {
        var doc = await Parse(@"Put it before the \verb|\end{document}| command.

\section{Next}
After.");
        doc.Elements.OfType<ImportParagraph>().First().Text.Should().Be("Put it before the `\\end{document}` command.");
        doc.Elements.OfType<ImportHeading>().Select(h => h.Text).Should().Contain("Next");
    }

    [Fact]
    public async Task Onlinecite_stays_a_citation()
    {
        var doc = await Parse(@"See Ref.~[\onlinecite{witten2001}] here.");
        doc.Elements.OfType<ImportParagraph>().Single().Text.Should().Contain(@"\onlinecite{witten2001}");
    }

    [Fact]
    public async Task A_caption_keeps_its_references_and_its_label_once()
    {
        var doc = await Parse(@"\begin{table}
\caption{\label{tab:widgets}Steps of \Cref{algo:1,algo:2}.}
\begin{tabular}{cc} a & b \\ \end{tabular}
\end{table}");
        var table = doc.Elements.OfType<ImportTable>().Single();
        table.Caption.Should().Be(@"Steps of \Cref{algo:1,algo:2}.");
        table.Label.Should().Be("tab:widgets");
    }

    [Fact]
    public async Task A_title_drops_its_size_switches()
    {
        var doc = await new LatexParser().ParseTextAsync(@"\documentclass{article}
\title{\LARGE \bf Residual-Guided Control}
\begin{document}\maketitle Body.\end{document}");
        doc.Title.Should().Be("Residual-Guided Control");
    }
}
