using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// An LML @algorithm opened as an empty block: the reader kept its text as <c>code</c>, and the editor reads
/// <c>lines</c> (the launch bot's #52 retest, 11 Nov 2026). The LML writer dropped an editor algorithm's lines too.
/// </summary>
public class LmlAlgorithmLinesTests
{
    private static JsonElement LinesOf(string lml)
    {
        var block = new LmlTextParser().Parse(lml).Blocks.Single();
        block.Type.Should().Be("algorithm");
        return JsonSerializer.SerializeToElement(block.Content).GetProperty("lines");
    }

    private static (int Indent, string Keyword, string Text, string Comment) Line(JsonElement l) => (
        l.GetProperty("indent").GetInt32(), l.GetProperty("keyword").GetString()!,
        l.GetProperty("text").GetString()!, l.GetProperty("comment").GetString()!);

    [Fact]
    public void Algorithmic_commands_become_the_editors_lines_nested_by_their_openers()
    {
        var lines = LinesOf(string.Join("\n",
            "@algorithm[caption=\"Sum\"]",
            "  \\REQUIRE $n \\ge 0$",
            "  \\STATE $s \\gets 0$",
            "  \\FOR{$i = 1$ \\TO $n$}",
            "    \\STATE $s \\gets s + i$",
            "  \\ENDFOR",
            "  \\RETURN $s$"));
        lines.EnumerateArray().Select(Line).Should().Equal(
            (0, "input", "$n \\ge 0$", ""),
            (0, "", "$s \\gets 0$", ""),
            (0, "for", "$i = 1$ \\TO $n$", ""),
            (1, "", "$s \\gets s + i$", ""),
            (0, "end", "", ""),
            (0, "return", "$s$", ""));
    }

    [Fact]
    public void Plain_lines_keep_their_nesting_keyword_and_comment()
    {
        var lines = LinesOf(string.Join("\n",
            "@algorithm",
            "  for each item x in L",
            "    if x > best  // a new maximum",
            "      best = x",
            "    end",
            "  end",
            "  return best"));
        lines.EnumerateArray().Select(Line).Should().Equal(
            (0, "for", "each item x in L", ""),
            (1, "if", "x > best", "a new maximum"),
            (2, "", "best = x", ""),
            (1, "end", "", ""),
            (0, "end", "", ""),
            (0, "return", "best", ""));
    }

    [Fact]
    public void An_algorithm_made_in_the_editor_goes_to_LML_and_back_with_its_lines()
    {
        var made = new[]
        {
            new { indent = 0, keyword = "while", text = "not done", comment = "" },
            new { indent = 1, keyword = "", text = "step()", comment = "one step" },
            new { indent = 0, keyword = "end", text = "", comment = "" },
        };
        var block = new Block
        {
            Id = Guid.NewGuid(),
            Type = "algorithm",
            Content = JsonDocument.Parse(JsonSerializer.Serialize(new { caption = "Loop", label = "", lines = made })),
        };
        var lml = new RenderService(null!, NullLogger<RenderService>.Instance).RenderBlockToLml(block);
        lml.Should().Contain("while not done").And.Contain("  step()  // one step");

        LinesOf(lml).EnumerateArray().Select(Line).Should().Equal(
            (0, "while", "not done", ""),
            (1, "", "step()", "one step"),
            (0, "end", "", ""));
    }
}
