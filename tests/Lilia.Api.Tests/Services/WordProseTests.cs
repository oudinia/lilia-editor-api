using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The Word export printed the LaTeX in prose as typed (13 Oct sweep, LTX-51:
/// "\SI{0.20}{\metre}"). ProseLatex reads it for Word; maths still becomes equations.
/// </summary>
public class WordProseTests
{
    private static readonly Func<string, string?> Labels = k => k switch { "sec:unc" => "3", "eq:fit" => "2", _ => null };

    [Theory]
    [InlineData(@"between \SI{0.20}{\metre} and \SI{1.20}{\metre}", "between 0.20 m and 1.20 m")]
    [InlineData(@"\qty{9.81}{\metre\per\second\squared}", "9.81 m/s²")]
    [InlineData(@"\SI{3}{\kilo\gram}, \num{12345.678}", "3 kg, 12345.678")]
    [InlineData(@"(Section~\ref{sec:unc})", "(Section 3)")]
    [InlineData(@"by \eqref{eq:fit}", "by (2)")]
    [InlineData(@"see \ref{sec:none}", "see [sec:none]")]
    [InlineData(@"Theorem~(sec:unc)", "Theorem 3")]
    [InlineData(@"as in \cite{knuth84,lamport94}", "as in [knuth84, lamport94]")]
    [InlineData(@"50\% done -- mostly", "50% done – mostly")]
    [InlineData(@"An \unknowncmd{kept} word", "An kept word")]
    public void Prose_latex_reads_for_word(string text, string expected) =>
        ProseLatex.ForWord(text, Labels).Should().Be(expected);

    [Fact]
    public void Marks_become_word_formatting_and_maths_stays_an_equation()
    {
        var spans = DocumentExportService.ParseInlineFormatting(@"We obtain \textbf{Splice} with $g = 9.8$ and \emph{care}.", Labels);
        spans.Should().Contain(s => s.Bold && s.Text == "Splice");
        spans.Should().Contain(s => s.Italic && s.Text == "care");
        spans.Should().Contain(s => s.Equation == "g = 9.8");
        spans.Should().NotContain(s => s.Text.Contains('\\'));
    }

    [Fact]
    public void A_price_stays_a_price()
    {
        var spans = DocumentExportService.ParseInlineFormatting("It costs $5 and the deluxe costs $10.", null);
        spans.Should().NotContain(s => s.Equation != null);
        string.Concat(spans.Select(s => s.Text)).Should().Be("It costs $5 and the deluxe costs $10.");
    }

    [Fact]
    public void A_plain_field_drops_the_marks() =>
        ProseLatex.PlainForWord(@"Growth of \textbf{bold} at \SI{5}{\percent} for \$3", null).Should().Be("Growth of bold at 5 % for $3");
}
