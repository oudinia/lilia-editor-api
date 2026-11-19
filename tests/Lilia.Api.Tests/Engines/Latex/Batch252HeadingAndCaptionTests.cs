using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #252 and #253 (19 Nov 2026), both from REVTeX's samples.
/// #252: a table caption's "\textbackslash\texttt{multicolumn}" was written back as \textbackslashmulticolumn. The caption
/// reader flattened every \texttt to its word, so the backslash command ran into it, and the PDF stopped ("Undefined control
/// sequence"). In running text, \textbackslash became a bare \, which the export reads as the start of a command too.
/// #253: a heading's forced break "\protect\\" was dropped but its \protect stayed, and swallowed the space after it in the
/// PDF ("First heading:The line"); the heading's "\textbackslash\textbackslash" became \\ and went as a break as well.
/// The break is now a space (a heading is one line in Lilia), and a \textbackslash stays \textbackslash{}.
/// </summary>
public class Batch252HeadingAndCaptionTests
{
    private static readonly RenderService Render = new(null!, NullLogger<RenderService>.Instance);

    private static async Task<ImportDocument> Parse(string body) =>
        await new LatexParser().ParseTextAsync("\\documentclass{revtex4-2}\n\\begin{document}\n" + body + "\n\\end{document}\n");

    /// <summary>The imported document's blocks as the PDF compile and get_lilia_latex write them.</summary>
    private static List<string> Latex(ImportDocument doc) =>
        LatexImportJobExecutor.MapElements(doc.Elements)
            .Select(b => Render.RenderBlockToLatex(B(b.type, b.content)))
            .ToList();

    private const string RevtexTable = @"\begin{table*}
\caption{\label{tab:table3}This is a wide table that spans the page
width in \texttt{twocolumn} mode. It is formatted using the
\texttt{table*} environment. It also demonstrates the use of
\textbackslash\texttt{multicolumn} in rows with entries that span
more than one column.}
\begin{tabular}{cc}
a & b \\
1 & 2 \\
\end{tabular}
\end{table*}";

    [Fact]
    public async Task A_backslash_before_a_typewriter_word_in_a_table_caption_is_written_back_as_the_author_wrote_it()
    {
        var doc = await Parse(RevtexTable);
        var table = doc.Elements.OfType<ImportTable>().Single();
        table.Label.Should().Be("tab:table3");
        var latex = Latex(doc).Single(l => l.Contains(@"\caption"));
        latex.Should().Contain(@"\textbackslash\texttt{multicolumn} in rows").And.NotContain(@"\textbackslashmulticolumn");
    }

    [Fact]
    public async Task The_other_typewriter_words_in_the_caption_stay_typewriter()
    {
        var latex = Latex(await Parse(RevtexTable)).Single(l => l.Contains(@"\caption"));
        latex.Should().Contain(@"in \texttt{twocolumn} mode").And.Contain(@"using the \texttt{table*} environment");
    }

    [Fact]
    public async Task A_backslash_before_a_typewriter_word_in_a_paragraph_stays_a_printed_backslash()
    {
        var doc = await Parse(@"It also demonstrates the use of \textbackslash\texttt{multicolumn} in rows.");
        var latex = string.Join("\n", Latex(doc));
        latex.Should().Contain(@"the use of \textbackslash{}\texttt{multicolumn} in rows").And.NotContain(@"\\texttt");
    }

    [Fact]
    public async Task A_backslash_before_a_word_keeps_the_word_apart_and_takes_the_space_tex_takes()
    {
        var doc = await Parse(@"Type \textbackslash begin or \textbackslash{}end here.");
        doc.Elements.OfType<ImportParagraph>().Single().Text.Should().Be(@"Type \textbackslash{}begin or \textbackslash{}end here.");
    }

    [Fact]
    public async Task A_forced_break_in_a_heading_is_one_space_and_leaves_no_protect()
    {
        var doc = await Parse(@"\section{\label{sec:one}First heading:\protect\\ The line
break was forced \protect\\ via}
Text.");
        var heading = doc.Elements.OfType<ImportHeading>().Single();
        heading.Text.Should().NotContain(@"\protect").And.NotContain(@"\\");
        Latex(doc).Should().Contain(l => l.StartsWith(@"\section{") && l.Contains("First heading: The line break was forced via}"));
    }

    [Fact]
    public async Task The_revtex_sample_heading_keeps_its_two_printed_backslashes()
    {
        var doc = await Parse(@"\section{\label{sec:level1}First-level heading:\protect\\ The line
break was forced \lowercase{via} \textbackslash\textbackslash}
Text.");
        var latex = Latex(doc).Single(l => l.StartsWith(@"\section"));
        latex.Should().Contain(@"First-level heading: The line break was forced via \textbackslash{}\textbackslash{}}")
            .And.NotContain(@"\protect");
    }
}
