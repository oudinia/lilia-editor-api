using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Engines.Themes;

/// <summary>
/// The launch bot's #247 (19 Nov 2026), from the batch 20 deck check: a frame holding a table stopped the deck ("Illegal
/// parameter number": the table's \providecommand{…}[1]{#1} sat in the frame's body), a list's \setcounter{enumi}{4} was
/// lost (1. Five), and beamer's block and alertblock were flattened, their titles gone.
/// </summary>
public class Batch21DeckTests
{
    private static Document Deck(params Block[] blocks) => new()
    {
        Id = Guid.NewGuid(), Title = "Deck", OwnerId = "u", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
        LatexDocumentClass = "beamer", LatexEngine = "pdflatex",
        Blocks = blocks.Select((b, i) => { b.SortOrder = i; return b; }).ToList(),
        BibliographyEntries = new List<BibliographyEntry>(),
    };

    private static string Preview(Document doc) => new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);

    private static string Export(Document doc) => new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
        .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions { CompileHere = true });

    [Fact]
    public void A_table_in_a_frame_defines_nothing_with_a_parameter()
    {
        var doc = Deck(
            B("heading", new { text = "Accuracy", level = 1, numbered = false }),
            B("table", new { caption = "Top-1", headers = new[] { "Model", "Acc" }, rows = new[] { new[] { "A", "82" } } }));
        foreach (var tex in new[] { Preview(doc), Export(doc) })
        {
            var frame = tex[tex.IndexOf(@"\frametitle{Accuracy}", StringComparison.Ordinal)..];
            frame = frame[..frame.IndexOf(@"\end{frame}", StringComparison.Ordinal)];
            frame.Should().Contain(@"\liliaTableHead{").And.NotContain("#1");
        }
    }

    [Fact]
    public void A_frame_holding_a_definition_with_a_parameter_is_fragile()
    {
        var doc = Deck(
            B("heading", new { text = "Macros", level = 1, numbered = false }),
            B("embed", new { engine = "latex", code = @"\newcommand{\twice}[1]{#1#1}\twice{ab}" }));
        Preview(doc).Should().Contain(@"\begin{frame}[fragile]\frametitle{Macros}");
    }

    [Fact]
    public async Task A_lists_start_number_is_kept()
    {
        var doc = await new LatexParser().ParseTextAsync(@"\documentclass{beamer}
\begin{document}
\begin{frame}{Start at five}
\begin{enumerate}\setcounter{enumi}{4}\item Five \item Six\end{enumerate}
\end{frame}
\end{document}");
        doc.Elements.OfType<ImportListItem>().Select(i => i.StartNumber).Should().AllBeEquivalentTo(5);
        var list = LatexImportJobExecutor.MapElements(doc.Elements).Single(b => b.type == "list");
        JsonSerializer.Serialize(list.content).Should().Contain("\"start\":5");

        var deck = Deck(B("list", new { items = new[] { "Five", "Six" }, ordered = true, start = 5 }));
        Preview(deck).Should().Contain("start=5");
    }

    [Fact]
    public async Task Beamer_blocks_keep_their_titles_in_a_deck()
    {
        var doc = await new LatexParser().ParseTextAsync(@"\documentclass{beamer}
\begin{document}
\begin{frame}{Results}
\begin{block}{Result}Fizzing yak block body.\end{block}
\begin{alertblock}{Careful}Muted owl alert.\end{alertblock}
\end{frame}
\end{document}");
        var raw = string.Join("\n", doc.Elements.OfType<ImportLatexPassthrough>().Select(p => p.LatexCode));
        raw.Should().Contain(@"\begin{block}{Result}Fizzing yak block body.\end{block}")
            .And.Contain(@"\begin{alertblock}{Careful}Muted owl alert.\end{alertblock}");
        doc.Elements.OfType<ImportParagraph>().Should().NotContain(p => p.Text.Contains("Fizzing yak"));
    }
}
