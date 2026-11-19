using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #248 (19 Nov 2026): a \[…\] display inside an \item lost its \frac, \sqrt and \mbox on import,
/// "x=a^22b, \quadas r", and the document stopped on an undefined control sequence.
/// </summary>
public class Batch23ListMathTests
{
    [Fact]
    public async Task A_display_in_a_list_item_keeps_its_commands()
    {
        var doc = await new LatexParser().ParseTextAsync(@"\documentclass{amsart}
\begin{document}
\begin{enumerate}
\item First item with a display
\[
x=\frac{a^2}2b,
\quad\mbox{as }r\to\infty,
\]
and text after.
\item Second
\[
y= o\left(\frac{f}{\sqrt{b}}\right)
\]
\item Inline \(\frac{1}{2}\) and an aligned one
\begin{align*} u &= \sqrt{v} \end{align*}
\end{enumerate}
\end{document}");
        var items = doc.Elements.OfType<ImportListItem>().Select(i => i.Text).ToList();
        items[0].Should().Contain(@"x=\frac{a^2}2b,").And.Contain(@"\quad\mbox{as }r\to\infty,").And.Contain("and text after.");
        items[1].Should().Contain(@"y= o\left(\frac{f}{\sqrt{b}}\right)");
        items[2].Should().Contain(@"$\frac{1}{2}$").And.Contain(@"u &= \sqrt{v}");
    }

    [Fact]
    public async Task A_line_break_with_a_skip_is_not_a_display()
    {
        var doc = await new LatexParser().ParseTextAsync(@"\documentclass{article}
\begin{document}
\begin{itemize}
\item One line\\[3pt] and \textbf{the next}
\end{itemize}
\end{document}");
        doc.Elements.OfType<ImportListItem>().Single().Text.Should().Contain("**the next**");
    }
}
