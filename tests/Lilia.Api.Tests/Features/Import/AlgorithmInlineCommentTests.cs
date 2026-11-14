using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// <c>\State x \Comment{why}</c> is one line with its comment, as algorithmicx prints it. The import made
/// the comment a second, numbered line, so every later line number moved (MCP-121 retest, 14 Nov 2026).
/// A <c>\Comment</c> alone on its source line stays a line of its own.
/// </summary>
public class AlgorithmInlineCommentTests
{
    private const string Body = """
        \begin{algorithm}
        \caption{Training loop}
        \begin{algorithmic}[1]
          \State $\theta \gets \theta_0$ \Comment{initialise}
          \For{$t = 1$ to $T$}
            \State $g \gets \nabla L(\theta)$
          \EndFor
          \Comment{done}
          \State \Return $\theta$
        \end{algorithmic}
        \end{algorithm}
        """;

    private static async Task<List<(string Keyword, string Text, string Comment)>> LinesAsync()
    {
        var parsed = await new LatexParser().ParseTextAsync("\\documentclass{article}\n\\usepackage{algpseudocode}\n\\begin{document}\n" + Body + "\n\\end{document}\n");
        var algo = parsed.Elements.OfType<ImportAlgorithm>().Single();
        var content = JsonSerializer.SerializeToElement(LatexImportJobExecutor.AlgorithmContent(algo));
        return content.GetProperty("lines").EnumerateArray()
            .Select(l => (l.GetProperty("keyword").GetString()!, l.GetProperty("text").GetString()!, l.GetProperty("comment").GetString()!))
            .ToList();
    }

    [Fact]
    public async Task A_comment_after_a_statement_belongs_to_that_line()
    {
        var lines = await LinesAsync();
        lines[0].Should().Be(("", @"$\theta \gets \theta_0$", "initialise"));
        lines.Should().HaveCount(6, "statement, for, statement, end, the lone comment, return");
        lines[4].Should().Be(("", "", "done"), "a comment alone on its line stays a line");
        lines[5].Should().Be(("return", @"$\theta$", ""), "\\State \\Return is one line");
    }

    [Fact]
    public void The_lml_reader_does_the_same()
    {
        var lines = JsonSerializer.SerializeToElement(AlgorithmLines.FromLml("\\STATE x \\gets 0 \\COMMENT{start}\n\\COMMENT{alone}\n\\STATE \\RETURN x"));
        lines.GetArrayLength().Should().Be(3);
        lines[2].GetProperty("keyword").GetString().Should().Be("return");
        lines[0].GetProperty("comment").GetString().Should().Be("start");
        lines[0].GetProperty("text").GetString().Should().Be(@"x \gets 0");
        lines[1].GetProperty("comment").GetString().Should().Be("alone");
    }
}
