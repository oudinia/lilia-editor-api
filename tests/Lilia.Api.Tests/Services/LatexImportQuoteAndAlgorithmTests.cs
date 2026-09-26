using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// quote, quotation, verse and algorithm arrive as the blocks the editor has.
///
/// <para>Measured by the LaTeX feature-coverage e2e: the parser recognised all
/// four environments, and the import job mapped each to an empty paragraph —
/// the element types were missing from its block mapping, so the text was
/// thrown away and the exporter had nothing to write.</para>
/// </summary>
public class LatexImportQuoteAndAlgorithmTests
{
    private static async Task<List<(string Type, JsonElement Content)>> Import(string body)
    {
        var doc = await new LatexParser().ParseTextAsync(
            "\\documentclass{article}\n\\usepackage{algorithm}\n\\usepackage{algpseudocode}\n\\begin{document}\n" +
            body + "\n\\end{document}\n");
        return LatexImportJobExecutor.MapElements(doc.Elements)
            .Select(b => (b.type, JsonSerializer.SerializeToElement(b.content)))
            .ToList();
    }

    [Theory]
    [InlineData("quote", "To be or not to be.")]
    [InlineData("quotation", "A longer quoted passage.")]
    public async Task A_quote_is_a_blockquote_with_its_text(string env, string text)
    {
        var blocks = await Import($"\\begin{{{env}}}\n{text}\n\\end{{{env}}}");

        blocks.Should().ContainSingle();
        blocks[0].Type.Should().Be("blockquote");
        blocks[0].Content.GetProperty("text").GetString().Should().Be(text);
    }

    [Fact]
    public async Task A_verse_keeps_its_lines()
    {
        var blocks = await Import("\\begin{verse}\nLine one \\\\\nLine two\n\\end{verse}");

        blocks.Should().ContainSingle();
        blocks[0].Type.Should().Be("blockquote");
        blocks[0].Content.GetProperty("variant").GetString().Should().Be("verse");
        blocks[0].Content.GetProperty("text").GetString().Should().Be("Line one\nLine two");
    }

    [Fact]
    public async Task An_algorithm_is_an_algorithm_block_with_its_caption_and_nested_lines()
    {
        var blocks = await Import("""
            \begin{algorithm}
            \caption{Sum}\label{alg:sum}
            \begin{algorithmic}
            \State $s \gets 0$
            \For{$i = 1$ to $n$}
            \State $s \gets s + i$
            \EndFor
            \end{algorithmic}
            \end{algorithm}
            """);

        blocks.Should().ContainSingle();
        blocks[0].Type.Should().Be("algorithm");
        var content = blocks[0].Content;
        content.GetProperty("caption").GetString().Should().Be("Sum");
        content.GetProperty("label").GetString().Should().Be("alg:sum");
        var lines = content.GetProperty("lines").EnumerateArray()
            .Select(l => (l.GetProperty("indent").GetInt32(), l.GetProperty("keyword").GetString(), l.GetProperty("text").GetString()))
            .ToList();
        lines.Should().Equal(
            (0, "", "$s \\gets 0$"),
            (0, "for", "$i = 1$ to $n$"),
            (1, "", "$s \\gets s + i$"),
            (0, "end", ""));
    }
}
