using FluentAssertions;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// MCP-92 (run 3, LTX-97): a \label after a verbatim embed, with nothing numbered before it, gives
/// \ref nothing to print ("see also Listing ."), and check_document didn't flag it.
/// </summary>
public class UnnumberedLabelTests
{
    private const string Preamble = "\\documentclass{report}\n\\newtheorem{theorem}{Theorem}\n\\newtheorem*{note}{Note}\n\\begin{document}\n";

    private static EmittedLatexAudit Audit(string body) => new(Preamble + body + "\n\\end{document}\n");

    [Fact]
    public void A_label_after_a_verbatim_embed_at_the_top_prints_nothing()
    {
        var a = Audit("By Theorem~\\ref{thm:a}, see Listing~\\ref{lst:embed}.\n\\begin{theorem}\n\\label{thm:a}\nX.\n\\end{theorem}\n\\begin{verbatim}\nx\n\\end{verbatim}\n\\label{lst:embed}");
        a.EmptyReferences.Select(r => r.Key).Should().BeEquivalentTo(["lst:embed"]);
    }

    [Theory]
    [InlineData("\\section{Intro}\\label{sec:a}")]
    [InlineData("\\begin{figure}\n\\caption{C}\\label{sec:a}\n\\end{figure}")]
    [InlineData("\\begin{equation}\\label{sec:a}\nx\n\\end{equation}")]
    [InlineData("\\begin{theorem}\n\\begin{itemize}\\item \\label{sec:a}\\end{itemize}\n\\end{theorem}")]
    [InlineData("\\begin{enumerate}\n\\item First\\label{sec:a}\n\\end{enumerate}")]
    [InlineData("\\chapter{One}\n\\begin{verbatim}\nx\n\\end{verbatim}\n\\label{sec:a}")]
    public void A_label_with_a_number_before_it_is_fine(string body) =>
        Audit("See \\ref{sec:a}.\n" + body).EmptyReferences.Should().BeEmpty();

    [Theory]
    [InlineData("\\begin{equation*}\\label{sec:a}\nx\n\\end{equation*}")]
    [InlineData("\\begin{note}\\label{sec:a}\nx\n\\end{note}")]
    [InlineData("\\section*{Intro}\\label{sec:a}")]
    [InlineData("\\begin{table}\n\\label{sec:a}\n\\end{table}")]
    public void A_label_in_something_unnumbered_is_flagged(string body) =>
        Audit("See \\ref{sec:a}.\n" + body).EmptyReferences.Should().ContainSingle();

    [Fact]
    public void A_pageref_still_prints_a_page() =>
        Audit("Page \\pageref{x}.\n\\label{x}").EmptyReferences.Should().BeEmpty();
}
