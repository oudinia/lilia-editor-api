using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// A .tex import keeps what makes the paper cross-referenceable and readable.
///
/// <para>Measured 26 Sep through the import page (e2e import-journey): the
/// parser lifted an equation's <c>\label</c> out of its body "so the editor
/// can store it" — and then dropped it, so every <c>\ref</c> to it dangled;
/// a table lost its <c>\caption</c> and <c>\label</c>; and an itemize of two
/// items arrived as two lists of one.</para>
/// </summary>
public class LatexImportKeepsStructureTests
{
    private const string Paper = """
        \documentclass{article}
        \begin{document}
        \begin{equation}
        E = mc^2
        \label{eq:energy}
        \end{equation}
        \begin{itemize}
          \item First finding
          \item Second finding
        \end{itemize}
        \begin{enumerate}
          \item One
        \end{enumerate}
        \begin{table}[h]
        \centering
        \begin{tabular}{ll}
        Model & Score \\
        ResNet & 76.1 \\
        \end{tabular}
        \caption{Scores by \textbf{model}}
        \label{tab:scores}
        \end{table}
        \end{document}
        """;

    private static async Task<List<(string Type, JsonElement Content)>> Blocks()
    {
        var doc = await new LatexParser().ParseTextAsync(Paper);
        return LatexImportJobExecutor.MapElements(doc.Elements)
            .Select(b => (b.type, JsonSerializer.SerializeToElement(b.content)))
            .ToList();
    }

    [Fact]
    public async Task The_equation_keeps_its_label_outside_its_source()
    {
        var eq = (await Blocks()).Single(b => b.Type == "equation").Content;
        eq.GetProperty("label").GetString().Should().Be("eq:energy");
        eq.GetProperty("latex").GetString().Should().NotContain(@"\label");
    }

    [Fact]
    public async Task The_table_keeps_its_caption_and_label()
    {
        var table = (await Blocks()).Single(b => b.Type == "table").Content;
        table.GetProperty("caption").GetString().Should().Be("Scores by model");
        table.GetProperty("label").GetString().Should().Be("tab:scores");
    }

    [Fact]
    public async Task A_list_is_one_block_and_two_lists_stay_two()
    {
        var lists = (await Blocks()).Where(b => b.Type == "list").Select(b => b.Content).ToList();
        lists.Should().HaveCount(2);
        lists[0].GetProperty("items").EnumerateArray().Select(i => i.GetString())
            .Should().Equal("First finding", "Second finding");
        lists[0].GetProperty("ordered").GetBoolean().Should().BeFalse();
        lists[1].GetProperty("items").EnumerateArray().Select(i => i.GetString()).Should().Equal("One");
        lists[1].GetProperty("ordered").GetBoolean().Should().BeTrue();
    }
}
