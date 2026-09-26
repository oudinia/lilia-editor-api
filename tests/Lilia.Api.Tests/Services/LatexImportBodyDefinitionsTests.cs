using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Definitions written in the document body are definitions, not text.
///
/// <para>Found by the LaTeX feature-coverage e2e: <c>\def\ours{our method}</c>
/// leaked as "\defour method"; <c>\theoremstyle{definition}</c> as a paragraph
/// reading "definition"; and a <c>\newcommand</c> in the body was stripped from
/// the text but never reached the document's custom preamble, so its uses were
/// undefined and the export did not compile (math.newcommand-macro,
/// advanced.def, theorems.definition).</para>
/// </summary>
public class LatexImportBodyDefinitionsTests
{
    private const string Source = """
        \documentclass{article}
        \newcommand{\pre}{P}
        \begin{document}
        \newcommand{\R}{\mathbb{R}}
        \def\ours{our method}
        \theoremstyle{definition}
        \DeclareMathOperator{\Tr}{Tr}
        Let $x \in \R$.
        \end{document}
        """;

    [Fact]
    public async Task No_definition_leaks_into_block_text()
    {
        var doc = await new LatexParser().ParseTextAsync(Source);

        var texts = doc.Elements.OfType<ImportParagraph>().Select(p => p.Text).ToList();
        texts.Should().Equal("Let $x \\in \\R$.");
    }

    [Fact]
    public void Body_macros_reach_the_custom_preamble_in_source_order()
    {
        var preamble = LatexPreambleExtractor.Extract(Source).CustomPreamble;

        preamble.Should().NotBeNull();
        preamble!.Split('\n').Select(l => l.Trim()).Should().Equal(
            @"\newcommand{\pre}{P}",
            @"\newcommand{\R}{\mathbb{R}}",
            @"\def\ours{our method}",
            @"\DeclareMathOperator{\Tr}{Tr}");
    }
}
