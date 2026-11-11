using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Engines;

/// <summary>
/// The editor's algorithm lines ({indent, keyword, text, comment}) as algorithmic LaTeX (the launch bot's #64, 11 Nov
/// 2026): every one came out as \STATE, so an algorithm written in Lilia lost its structure in the PDF and its "end"
/// rows printed as empty steps.
/// </summary>
public class AlgorithmEditorLinesLatexTests
{
    private static string Latex(params object[] lines)
    {
        var block = new Block
        {
            Id = Guid.NewGuid(),
            Type = "algorithm",
            Content = JsonDocument.Parse(JsonSerializer.Serialize(new { caption = "Sum", label = "", lines })),
        };
        return new RenderService(null!, NullLogger<RenderService>.Instance).RenderBlockToLatex(block);
    }

    private static object L(int indent, string keyword, string text = "", string comment = "") => new { indent, keyword, text, comment };

    private static string[] Steps(string latex) => latex.Split('\n').Select(l => l.Trim())
        .SkipWhile(l => !l.StartsWith(@"\begin{algorithmic}")).Skip(1).TakeWhile(l => !l.StartsWith(@"\end{algorithmic}")).ToArray();

    [Fact]
    public void A_for_loop_with_an_end_row_keeps_its_structure_and_prints_no_empty_step()
    {
        Steps(Latex(L(0, "input", "$n \\ge 0$"), L(0, "", "$s \\gets 0$"), L(0, "for", "$i = 1$ to $n$"),
                L(1, "", "$s \\gets s + i$"), L(0, "end"), L(0, "return", "$s$")))
            .Should().Equal(@"\REQUIRE $n \ge 0$", @"\STATE $s \gets 0$", @"\FOR{$i = 1$ to $n$}", @"\STATE $s \gets s + i$", @"\ENDFOR", @"\RETURN $s$");
    }

    [Fact]
    public void If_else_if_else_close_once_and_comments_ride_along()
    {
        Steps(Latex(L(0, "if", "$x > 0$"), L(1, "", "positive", "sign"), L(0, "else if", "$x < 0$"), L(1, "", "negative"),
                L(0, "else"), L(1, "", "zero"), L(0, "end")))
            .Should().Equal(@"\IF{$x > 0$}", @"\STATE positive \COMMENT{sign}", @"\ELSIF{$x < 0$}", @"\STATE negative",
                @"\ELSE", @"\STATE zero", @"\ENDIF");
    }

    [Fact]
    public void Blocks_left_open_close_by_indentation_and_at_the_end()
    {
        Steps(Latex(L(0, "while", "not done"), L(1, "for", "each item"), L(2, "", "work"), L(0, "", "after"), L(0, "repeat"), L(1, "", "try")))
            .Should().Equal(@"\WHILE{not done}", @"\FOR{each item}", @"\STATE work", @"\ENDFOR", @"\ENDWHILE", @"\STATE after",
                @"\REPEAT", @"\STATE try", @"\UNTIL{true}");
    }

    [Fact]
    public void Empty_rows_print_nothing_but_an_empty_algorithm_still_has_a_step()
    {
        Steps(Latex(L(0, ""), L(0, "", "only"), L(0, ""))).Should().Equal(@"\STATE only");
        Steps(Latex(L(0, ""))).Should().Equal(@"\STATE");
    }

    [Fact]
    public void The_importers_kind_shape_is_emitted_as_before()
    {
        Steps(Latex(new { kind = "for", text = "$i$" }, new { kind = "statement", text = "x" }, new { kind = "endfor", text = "" }))
            .Should().Equal(@"\FOR{$i$}", @"\STATE x", @"\ENDFOR");
    }
}
