using System.IO.Compression;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lilia.Import.Converters;
using Lilia.Import.Models;
using Lilia.Import.Services;
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

    [Fact]
    public void Siunitx_in_maths_is_spelled_for_the_equation_converter() =>
        ProseLatex.MathForWord(@"g = \SI{9.794 \pm 0.021}{\metre\per\second\squared}").Should().Be(@"g = 9.794 \pm 0.021\,\mathrm{m/s²}");

    /// <summary>The sweep's own inputs, through the real Word writer: no LaTeX left outside equations.</summary>
    [Fact]
    public async Task A_word_file_holds_no_latex_outside_its_equations()
    {
        List<ExportRichTextSpan> R(string t) => DocumentExportService.ParseInlineFormatting(t, Labels);
        var doc = new ExportDocument { Title = "Sweep" };
        doc.Blocks.Add(new ExportBlock { Type = "abstract", Content = new ExportBlockContent { RichText = R(@"we obtain $g = \SI{9.794 \pm 0.021}{\metre\per\second\squared}$ at $0.8\sigma$.") } });
        doc.Blocks.Add(new ExportBlock { Type = "theorem", Content = new ExportBlockContent { Text = "Theorem 1", RichText = R(@"For all $\epsilon > 0$ it holds.") } });
        doc.Blocks.Add(new ExportBlock { Type = "list", Content = new ExportBlockContent { Items = [new ExportListItem { Text = "x", RichText = R(@"Rate $\tau \le 1$") }] } });
        doc.Blocks.Add(new ExportBlock
        {
            Type = "table",
            Content = new ExportBlockContent
            {
                HasHeader = true,
                Rows =
                [
                    [new ExportTableCell { Text = "Name" }, new ExportTableCell { Text = "Spearman", RichText = R(@"Spearman $\rho$") }],
                    [new ExportTableCell { Text = @"\textbf{End-to-end}", RichText = R(@"\textbf{End-to-end}") }, new ExportTableCell { Text = @"$\si{\second\squared\per\metre}$", RichText = R(@"$\si{\second\squared\per\metre}$") }],
                ],
            },
        });

        doc.Blocks.Insert(0, new ExportBlock { Type = "title", Content = new ExportBlockContent { Text = "Sweep", Author = ProseLatex.PlainForWord("R. Okafor, M. Lindqvist") } });
        var bytes = await new DocxExportService(new LatexToOmmlConverter()).ExportAsync(doc);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = await reader.ReadToEndAsync();

        var outsideMaths = Regex.Replace(xml, @"<m:oMath\b.*?</m:oMath>", "", RegexOptions.Singleline);
        Regex.Matches(outsideMaths, @"<w:t[^>]*>([^<]*)</w:t>").Select(m => m.Groups[1].Value)
            .Should().NotContain(t => Regex.IsMatch(t, @"\\[A-Za-z]") || t.Contains('$'));
        Regex.Matches(xml, "<m:oMath\\b").Count.Should().BeGreaterThanOrEqualTo(6);
    }

    [Fact]
    public void Chemistry_reads_for_word()
    {
        ProseLatex.MathForWord(@"V(\ce{O2})").Should().Be(@"V(\mathrm{O_{2}})");
        ProseLatex.ForWord(@"Water is \ce{H2O}.").Should().Be("Water is H₂O.");
    }

    [Theory]
    [InlineData(@"Results for $\alpha = 0.05$ and $n^2$ samples", "Results for α = 0.05 and n² samples")]
    [InlineData(@"Error $x_1 \le \epsilon$", "Error x₁ ≤ ε")]
    [InlineData(@"Rate $\frac{a}{b}$", @"Rate $\frac{a}{b}$")]
    public void Simple_maths_in_a_plain_field_reads_as_text(string text, string expected) =>
        ProseLatex.PlainForWord(text).Should().Be(expected);

    [Theory]
    [InlineData(@"CJK: \begin{CJK}{UTF8}{gbsn}数学\end{CJK}.", "CJK: 数学.")]
    [InlineData(@"Cyrillic: \foreignlanguage{russian}{Привет}.", "Cyrillic: Привет.")]
    [InlineData(@"\textcolor{red}{warm}", "warm")]
    public void An_unknown_command_keeps_its_text(string text, string expected) =>
        ProseLatex.ForWord(text).Should().Be(expected);
}
