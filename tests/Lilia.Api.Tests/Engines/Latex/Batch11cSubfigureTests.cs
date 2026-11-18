using FluentAssertions;
using Lilia.Import.Converters;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's llncs paper (18 Nov 2026): a figure of subfigures in minipages inside an example spilled its
/// subfigures after the example as text ("subfigure[t]{\linewidth} …"), and the compile stopped (#202). And a task
/// item's box printed as the word "boxtimes" in Word (#201).
/// </summary>
public class Batch11cSubfigureTests
{
    private const string Source = """
        \documentclass{article}
        \newtheorem{example}{Example}
        \begin{document}
        \begin{example}
        \begin{figure}[ht]
        \centering
        \begin{minipage}[c]{0.4\textwidth}
        \begin{subfigure}[t]{\linewidth}
        \centering
        LEFT CONTENT
        \caption{Left}\label{fig:left}
        \end{subfigure}
        \end{minipage}%
        \hfill
        \begin{minipage}[c]{0.5\textwidth}
        \begin{subfigure}[t]{\linewidth}
        \centering
        RIGHT CONTENT
        \caption{Right}\label{fig:right}
        \end{subfigure}
        \end{minipage}
        \caption{Both halves.}\label{fig:both}
        \end{figure}
        Text after the figure, still in the example.
        \end{example}
        \end{document}
        """;

    [Fact]
    public async Task A_figure_in_an_example_stays_one_figure_with_its_subfigures()
    {
        var doc = await new LatexParser().ParseTextAsync(Source);
        var theorem = doc.Elements.OfType<ImportTheorem>().Single();
        theorem.Text.Should().Contain("Text after the figure").And.NotContain("subfigure").And.NotContain("minipage");
        var texts = doc.Elements.Select(e => e switch { ImportParagraph p => p.Text, _ => null }).Where(t => t is not null).ToList();
        texts.Should().NotContain(t => t!.Contains("subfigure") || t.Contains(@"\linewidth"));
        var figure = doc.Elements.OfType<ImportImage>().Single(i => i.Caption == "Both halves.");
        figure.Label.Should().Be("fig:both");
    }

    [Fact]
    public void Word_knows_the_task_box()
    {
        var (omml, ok, _) = new LatexToOmmlConverter().Convert(@"\boxtimes");
        ok.Should().BeTrue();
        omml.Should().Contain("⊠").And.NotContain("boxtimes");
    }
}
