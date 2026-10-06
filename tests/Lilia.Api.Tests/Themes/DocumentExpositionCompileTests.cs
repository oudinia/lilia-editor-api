using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.TexSafety;
using Microsoft.Extensions.Logging.Abstractions;
using static Lilia.Api.Tests.Themes.DocumentThemeCompileTests;

namespace Lilia.Api.Tests.Themes;

/// <summary>
/// Exposition compiled for real: a beamer deck built from Lilia's slide blocks (RenderSlideToLatex,
/// now SlideLatex.Frame) under Classic and Exposition, with pdfLaTeX and LuaLaTeX, through both
/// writers: the preview (RenderService.RenderToLatex) and the PDF export (LaTeXExportService,
/// compiled here). The compile service stages beamerthemeLiliaExposition.sty beside the .tex. Each
/// PDF must keep the deck's words (pdftotext) and, under Exposition, be set in Montserrat and
/// Josefin Sans (pdffonts) on the burgundy ground; print-safe puts it on cream with no gold.
///
/// <para>In the theme-availability collection: the real kpsewhich probe decides here, and the
/// unit tests that override it must not run at the same time.</para>
/// </summary>
[Trait("Category", "ThemeCompile")]
[Collection(ThemeAvailabilityCollection.Name)]
public class DocumentExpositionCompileTests
{
    private const string Burgundy = "#4A1B22";
    private const string Cream = "#F3E9DC";
    private const string Gold = "#EBC155";

    private static int _order;

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = _order++,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    private static List<Block> Deck() =>
    [
        B("heading", new { text = "Mixing", level = 1 }),
        B("slide", new { title = "The mixing matrix", content = "Quartz zebra slide with $\\theta_{12}$ and **bold** words." }),
        B("slide", new { title = "Two columns", layout = "two-column", content = "Jovial wombat left\n---\nFizzing yak right" }),
        B("heading", new { text = "Data", level = 1 }),
        B("slide", new { title = "Accuracy", subtitle = "Top-1", content = "Muted owl closes the deck.", notes = "Speak slowly." }),
    ];

    private static readonly string[] Words =
        ["Neutrino oscillations", "The mixing matrix", "Quartz zebra", "Jovial wombat", "Fizzing yak", "Accuracy", "Top-1", "Muted owl"];

    private static Document Doc(string theme, string engine, string paper = "theme") => new()
    {
        Id = Guid.NewGuid(), Title = "Neutrino oscillations", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
        LatexDocumentClass = "beamer",
        LatexEngine = engine,
        Look = theme == "classic" ? null : JsonSerializer.Serialize(new { theme, paper, pins = new { } }),
        Blocks = Deck(),
        BibliographyEntries = new List<BibliographyEntry>(),
    };

    /// <summary>The deck's LaTeX as the preview writes it, or as the PDF export does.</summary>
    private static string Latex(Document doc, string writer, Lilia.Engines.Themes.DocumentLook? lookOverride = null)
    {
        var tex = writer == "preview"
            ? new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc)
            : new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
                .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions { CompileHere = true, LookOverride = lookOverride });
        TexSourceGuard.Violation(tex).Should().BeNull("the guard must accept the generated deck");
        tex.Should().Contain(@"\documentclass[11pt,a4paper]{beamer}").And.Contain(@"\begin{frame}{The mixing matrix}");
        return tex;
    }

    private static async Task<byte[]> Compile(string tex, string engine) =>
        // Not tolerant: any LaTeX error fails the test.
        await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, engine, timeout: 180);

    public static TheoryData<string, string, string> Matrix()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var writer in new[] { "preview", "export" })
            foreach (var theme in new[] { "classic", "exposition" })
                foreach (var engine in new[] { "pdflatex", "lualatex" })
                    data.Add(writer, theme, engine);
        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task A_deck_of_slide_blocks_compiles_and_keeps_its_content(string writer, string theme, string engine)
    {
        var tex = Latex(Doc(theme, engine), writer);
        if (theme == "classic") tex.Should().NotContain("LiliaExposition").And.NotContain("{lilia-theme}");
        else tex.Should().Contain(@"\usetheme{LiliaExposition}").And.NotContain("printsafe");

        var pdf = await Compile(tex, engine);

        var text = await Tool("pdftotext", pdf, "-");
        foreach (var word in Words) text.Should().Contain(word, $"{writer}/{theme}/{engine} must keep the deck");

        var fonts = await Tool("pdffonts", pdf);
        var colours = FillColours(pdf);
        if (theme == "classic")
        {
            fonts.Should().NotContain("Montserrat").And.NotContain("Josefin", "Classic is beamer's own look");
            colours.Should().NotContain(c => Near(c, Burgundy));
        }
        else
        {
            fonts.Should().Contain("Montserrat", "Exposition's body is Montserrat");
            fonts.Should().Contain("JosefinSans", "Exposition's titles are Josefin Sans");
            colours.Should().Contain(c => Near(c, Burgundy), "the burgundy ground");
            colours.Should().Contain(c => Near(c, Gold), "gold frame titles and tabs");
            colours.Should().Contain(c => Near(c, Cream), "cream text");
            // The kicker: section number · frame number, then the section (beamer's default
            // look prints no section names).
            text.Should().Contain("1·2").And.Contain("Mixing").And.Contain("2·4").And.Contain("Data");
        }
    }

    [Theory]
    [InlineData("preview", "pdflatex")]
    [InlineData("export", "lualatex")]
    public async Task White_paper_prints_the_deck_print_safe(string writer, string engine)
    {
        var tex = Latex(Doc("exposition", engine, paper: "white"), writer);
        tex.Should().Contain(@"\usetheme[printsafe]{LiliaExposition}");
        await AssertPrintSafe(await Compile(tex, engine));
    }

    [Fact]
    public async Task A_print_safe_export_of_an_exposition_deck_prints_on_cream()
    {
        // Export PDF: Print-safe, on a deck whose own paper is the theme's.
        var doc = Doc("exposition", "pdflatex");
        var look = Lilia.Engines.Themes.DocumentLook.Parse(doc.Look).With(null, printSafe: true);
        var tex = Latex(doc, "export", look);
        tex.Should().Contain(@"\usetheme[printsafe]{LiliaExposition}");
        await AssertPrintSafe(await Compile(tex, "pdflatex"));
    }

    private static async Task AssertPrintSafe(byte[] pdf)
    {
        var text = await Tool("pdftotext", pdf, "-");
        foreach (var word in Words) text.Should().Contain(word);
        var fonts = await Tool("pdffonts", pdf);
        fonts.Should().Contain("Montserrat").And.Contain("JosefinSans");
        var colours = FillColours(pdf);
        colours.Should().Contain(c => Near(c, Cream), "cream paper");
        colours.Should().Contain(c => Near(c, Burgundy), "burgundy ink and structure");
        colours.Should().NotContain(c => Near(c, Gold), "gold does not hold on cream");
    }

    [Fact]
    public async Task The_authors_preamble_wins_over_exposition()
    {
        var doc = Doc("exposition", "pdflatex");
        doc.CustomPreamble = @"\setbeamercolor{frametitle}{fg=red}";
        var pdf = await Compile(Latex(doc, "preview"), "pdflatex");
        FillColours(pdf).Should().Contain(c => Near(c, "#FF0000"), "the author's frame title colour is what prints");
        (await Tool("pdftotext", pdf, "-")).Should().Contain("The mixing matrix");
    }
}
