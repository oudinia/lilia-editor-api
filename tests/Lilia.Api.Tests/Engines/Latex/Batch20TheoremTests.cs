using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Models;

using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #230 (19 Nov 2026): a paper's own theorem environments. \begin{prop} became Lilia's proposition with
/// a counter of its own, \newtheorem{corollary}[prop] lost its shared counter, \theoremstyle{remark} was lost to Lilia's
/// remark, and \begin{rem} was dropped to bare text. The PDF printed "Proposition 1 / Corollary 1 / Remark text."
/// </summary>
public class Batch20TheoremTests
{
    private const string Preamble = @"\newtheorem{prop}{Proposition}[section]
\newtheorem{corollary}[prop]{Corollary}
\theoremstyle{definition}
\newtheorem{df}[prop]{Definition}
\theoremstyle{remark}
\newtheorem{rem}{Remark}";

    [Fact]
    public async Task The_papers_own_environments_are_theorems_that_keep_their_names()
    {
        var doc = await new LatexParser().ParseTextAsync("\\documentclass{amsart}\n" + Preamble + @"
\begin{document}
\section{A}
\begin{df}Def text.\end{df}
\begin{prop}Prop text.\end{prop}
\begin{corollary}Cor text.\end{corollary}
\begin{rem}Remark text.\end{rem}
\end{document}");
        var theorems = doc.Elements.OfType<ImportTheorem>().Select(t => (t.EnvironmentName, t.EnvironmentType, t.Text)).ToList();
        theorems.Should().Equal(
            ("df", TheoremEnvironmentType.Definition, "Def text."),
            ("prop", TheoremEnvironmentType.Proposition, "Prop text."),
            ("corollary", TheoremEnvironmentType.Corollary, "Cor text."),
            ("rem", TheoremEnvironmentType.Remark, "Remark text."));
        doc.Elements.OfType<ImportParagraph>().Should().NotContain(p => p.Text.Contains("Remark text"));
    }

    private static Document Paper(string cls, string? preamble)
    {
        var blocks = new[]
        {
            B("heading", new { text = "A", level = 1 }),
            B("theorem", new { theoremType = "definition", text = "Def text.", environment = "df" }),
            B("theorem", new { theoremType = "proposition", text = "Prop text.", environment = "prop" }),
            B("theorem", new { theoremType = "corollary", text = "Cor text.", environment = "corollary" }),
            B("theorem", new { theoremType = "remark", text = "Remark text.", environment = "rem" }),
            B("theorem", new { theoremType = "lemma", text = "Lilia's own lemma." }),
        };
        return new Document
        {
            Id = Guid.NewGuid(), Title = "Theorems", OwnerId = "u", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
            LatexDocumentClass = cls, CustomPreamble = preamble,
            Blocks = blocks.Select((b, i) => { b.SortOrder = i; return b; }).ToList(),
            BibliographyEntries = new List<BibliographyEntry>(),
        };
    }

    private static string Preview(Document doc) => new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);

    private static string Export(Document doc) => new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
        .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions { CompileHere = true });

    [Theory]
    [InlineData("article")]
    [InlineData("amsart")]
    public void The_papers_declarations_win_and_its_environments_are_used(string cls)
    {
        foreach (var tex in new[] { Preview(Paper(cls, Preamble)), Export(Paper(cls, Preamble)) })
        {
            tex.Should().Contain(@"\newtheorem{corollary}[prop]{Corollary}", "the shared counter is kept");
            tex.Should().NotContain(@"\newtheorem{corollary}{Corollary}", "Lilia's own corollary would come first");
            tex.Should().Contain(@"\begin{df}").And.Contain(@"\begin{prop}").And.Contain(@"\begin{corollary}").And.Contain(@"\begin{rem}");
            tex.Should().Contain(@"\begin{lemma}", "a kind the paper doesn't declare is Lilia's");
            tex.Should().NotContain(@"left out \newtheorem");
        }
    }

    [Fact]
    public void Without_the_declaration_a_theorem_falls_back_to_lilias_kind()
    {
        foreach (var tex in new[] { Preview(Paper("article", null)), Export(Paper("article", null)) })
        {
            tex.Should().Contain(@"\begin{proposition}").And.Contain(@"\begin{remark}").And.Contain(@"\begin{definition}");
            tex.Should().NotContain(@"\begin{prop}").And.NotContain(@"\begin{rem}");
        }
    }
}
