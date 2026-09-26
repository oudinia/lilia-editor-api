using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Inline LaTeX the editor has its own node or mark for is imported in the
/// form the editor reads (content-converter.ts) and the exporter writes back.
///
/// <para>Found by the LaTeX feature-coverage e2e. Every cross-reference was
/// flattened to its bare label ("By eq:pyth the sides relate"); \href became a
/// Markdown link that neither the editor nor the exporter reads, so it printed
/// as "[the website](https://…)"; \url lost its link; \textsc lost its small
/// caps.</para>
/// </summary>
public class LatexImportEditorInlineMarksTests
{
    private static async Task<string> Paragraph(string body)
    {
        var doc = await new LatexParser().ParseTextAsync(
            "\\documentclass{article}\n\\begin{document}\n" + body + "\n\\end{document}\n");
        return doc.Elements.OfType<ImportParagraph>().Single().Text;
    }

    [Theory]
    [InlineData(@"By \eqref{eq:pyth} the sides relate.")]
    [InlineData(@"As in Section \ref{sec:setup}.")]
    [InlineData(@"See page \pageref{sec:setup}.")]
    [InlineData(@"See \autoref{sec:setup} and \cref{sec:setup}.")]
    [InlineData(@"See \href{https://example.org}{the website}.")]
    [InlineData(@"See \url{https://example.org/data}.")]
    public async Task Editor_native_inline_commands_are_kept(string text)
    {
        (await Paragraph(text)).Should().Be(text);
    }

    [Fact]
    public async Task A_non_breaking_space_before_a_reference_is_a_space()
    {
        (await Paragraph(@"Table~\ref{tab:soil} lists them.")).Should().Be(@"Table \ref{tab:soil} lists them.");
    }

    [Fact]
    public async Task Small_caps_become_the_editors_small_caps_mark()
    {
        (await Paragraph(@"The \textsc{Lilia} editor.")).Should().Be("The ^^Lilia^^ editor.");
    }

    [Fact]
    public async Task A_caption_still_gets_the_plain_label()
    {
        var doc = await new LatexParser().ParseTextAsync("""
            \documentclass{article}
            \begin{document}
            \begin{table}[h]
            \caption{Same as Table~\ref{tab:a}}
            \begin{tabular}{ll}
            a & b \\
            c & d
            \end{tabular}
            \end{table}
            \end{document}
            """);
        var table = LatexImportJobExecutor.MapElements(doc.Elements).Single(b => b.type == "table");
        JsonSerializer.SerializeToElement(table.content).GetProperty("caption").GetString()
            .Should().Be("Same as Table tab:a");
    }
}
