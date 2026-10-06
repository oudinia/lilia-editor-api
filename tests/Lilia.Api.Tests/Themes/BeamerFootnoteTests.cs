using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using static Lilia.Api.Tests.Themes.DocumentThemeCompileTests;

namespace Lilia.Api.Tests.Themes;

/// <summary>
/// Footnotes in beamer decks (6 Oct 2026). setspace, loaded for line spacing, rewrote the footnote
/// machinery beamer uses: a slide's \footnote printed its mark and never its text. And a footnote
/// block, written between frames, got a page of its own. Beamer decks now leave setspace out and
/// move each footnote block into the slide before it, in the preview and in the export.
/// </summary>
public class BeamerFootnoteTests
{
    private static int _order;

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = _order++,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    private static Document Deck(string cls = "beamer", double? lineSpacing = null) => new()
    {
        Id = Guid.NewGuid(), Title = "Footnote deck", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
        LatexDocumentClass = cls,
        LineSpacing = lineSpacing,
        Blocks =
        [
            B("slide", new { title = "Inline footnote", content = "Neutrinos oscillate\\footnote{Quartz zebra footnote text.} over distance." }),
            B("footnote", new { text = "Jovial wombat footnote block." }),
            B("slide", new { title = "Second", content = "Fizzing yak after the block." }),
        ],
        BibliographyEntries = new List<BibliographyEntry>(),
    };

    private static string Latex(Document doc, string writer) => writer == "preview"
        ? new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc)
        : new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions { CompileHere = true });

    [Theory]
    [InlineData("preview")]
    [InlineData("export")]
    public void A_deck_leaves_setspace_out_and_puts_the_footnote_block_inside_the_slide_before_it(string writer)
    {
        var tex = Latex(Deck(), writer);
        tex.Should().NotContain(@"\usepackage{setspace}");
        var frame = tex[tex.IndexOf(@"\begin{frame}{Inline footnote}", StringComparison.Ordinal)..];
        frame = frame[..frame.IndexOf(@"\end{frame}", StringComparison.Ordinal)];
        frame.Should().Contain(@"\footnote{Jovial wombat footnote block.}");
    }

    [Fact]
    public void An_article_keeps_setspace_and_its_footnote_block_where_it_was()
    {
        var doc = Deck("article");
        doc.Blocks = [B("paragraph", new { text = "Body." }), B("footnote", new { text = "Plain footnote." })];
        var tex = Latex(doc, "export");
        tex.Should().Contain(@"\usepackage{setspace}").And.Contain(@"\footnote{Plain footnote.}");
    }

    [Theory]
    [InlineData(1.5, @"\linespread{1.25}", @"\onehalfspacing")]
    [InlineData(2.0, @"\linespread{1.667}", @"\doublespacing")]
    public void A_decks_line_spacing_is_written_without_setspace(double spacing, string deckLine, string articleLine)
    {
        Latex(Deck(lineSpacing: spacing), "export").Should().Contain(deckLine).And.NotContain(articleLine);
        var article = Deck("article", spacing);
        article.Blocks = [B("paragraph", new { text = "Body." })];
        Latex(article, "export").Should().Contain(articleLine);
    }

    [Theory]
    [Trait("Category", "ThemeCompile")]
    [InlineData("preview", "pdflatex")]
    [InlineData("export", "pdflatex")]
    [InlineData("export", "lualatex")]
    public async Task Both_footnotes_print_and_the_deck_has_no_stray_page(string writer, string engine)
    {
        var doc = Deck();
        doc.LatexEngine = engine;
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance)
            .RenderToPdfAsync(Latex(doc, writer), engine, timeout: 180);

        var text = await Tool("pdftotext", pdf, "-");
        text.Should().Contain("Quartz zebra footnote text.", "a slide's own \\footnote prints its text");
        text.Should().Contain("Jovial wombat footnote block.", "the footnote block prints, on its slide");

        // The title frame and two slides: a footnote block between frames used to add a fourth page.
        var info = await Tool("pdfinfo", pdf);
        info.Should().MatchRegex(@"Pages:\s+3\b");
    }
}
