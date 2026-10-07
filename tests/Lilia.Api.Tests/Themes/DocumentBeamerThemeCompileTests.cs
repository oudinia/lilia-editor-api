using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.TexSafety;
using Lilia.Engines.Themes;
using Microsoft.Extensions.Logging.Abstractions;
using static Lilia.Api.Tests.Themes.DocumentThemeCompileTests;

namespace Lilia.Api.Tests.Themes;

/// <summary>
/// Cerulean and Index compiled for real as beamer themes (Olivia, 6 Oct 2026, REPLY-2026-10-06c):
/// a deck of Lilia blocks (heading blocks → <c>\section</c>, slide blocks → frames, embed blocks for
/// the contents frame, a block, an alertblock and a definition), with three numbered sections, a
/// starred one, a pinned one and an <c>\appendix</c>, under pdfLaTeX and LuaLaTeX, on the theme's
/// paper and print-safe. Each PDF must keep the deck's words (pdftotext), be set in Montserrat
/// (Source Serif only for the definition) and carry the theme's colours, read back from the PDF:
/// Index's sections in sequence order with the pin honoured, Cerulean's title frame on #08597F,
/// white when print-safe.
///
/// <para>In the theme-availability collection: the real kpsewhich probe decides here, and the
/// unit tests that override it must not run at the same time.</para>
/// </summary>
[Trait("Category", "ThemeCompile")]
[Collection(ThemeAvailabilityCollection.Name)]
public class DocumentBeamerThemeCompileTests
{
    private const string CeruleanTitle = "#08597F";
    private const string CeruleanAccent = "#0A76A4";
    private const string CeruleanInk = "#22262B";
    private const string Danger = "#C4314B";
    private const string Success = "#107C41";
    private const string Neutral = "#3A3836";
    private const string White = "#FFFFFF";
    private static readonly IReadOnlyList<string> Seq = ThemeCatalog.Sequence;

    private static int _order;

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = _order++,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    private static Block Embed(string code) => B("embed", new { engine = "latex", code });

    /// <summary>The deck, and the id of its second section (pinned to the eighth colour).</summary>
    private static (List<Block> Blocks, Guid Pinned) Deck(bool definition = true)
    {
        var data = B("heading", new { text = "Data", level = 1 });
        var blocks = new List<Block>
        {
            Embed("\\begin{frame}{Outline}\n\\tableofcontents\n\\end{frame}"),
            B("heading", new { text = "Mixing", level = 1 }),
            B("slide", new { title = "The mixing matrix", content = "Quartz zebra slide with $\\theta_{12}$ and **bold** words." }),
            Embed("\\begin{frame}{Blocks}\n\\begin{block}{Jovial block}Plain block body.\\end{block}\n" +
                  "\\begin{alertblock}{Alert owl}Careful with signs.\\end{alertblock}\n\\end{frame}"),
            data,
            B("slide", new { title = "Accuracy", subtitle = "Top-1", content = "Muted owl measures it." }),
            B("heading", new { text = "Results", level = 1 }),
            B("slide", new { title = "Results frame", content = "Jovial wombat results." }),
            B("heading", new { text = "Aside", level = 1, numbered = false }),
            B("slide", new { title = "Aside frame", content = "Fizzing yak aside." }),
            B("backMatter", new { subType = "appendix" }),
            B("heading", new { text = "Proofs", level = 1 }),
            B("slide", new { title = "Proof frame", content = "Velvet heron proof." }),
        };
        if (definition)
            blocks.Insert(6, Embed("\\begin{frame}{Definitions}\n\\begin{definition}A neutrino is a light lepton.\\end{definition}\n\\end{frame}"));
        return (blocks, data.Id);
    }

    private static readonly string[] Words =
        ["Neutrino oscillations", "Outline", "The mixing matrix", "Quartz zebra", "Jovial block", "Alert owl", "Careful with signs",
         "Accuracy", "Top-1", "Muted owl", "Jovial wombat", "Fizzing yak", "Velvet heron", "Mixing", "Data", "Results", "Proofs"];

    private static Document Doc(string theme, string engine, string paper = "theme", bool definition = true, string? preamble = null)
    {
        var (blocks, pinned) = Deck(definition);
        return new Document
        {
            Id = Guid.NewGuid(), Title = "Neutrino oscillations", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
            LatexDocumentClass = "beamer", LatexEngine = engine, CustomPreamble = preamble,
            Look = JsonSerializer.Serialize(new { theme, paper, pins = new Dictionary<string, int> { [pinned.ToString()] = 7 } }),
            Blocks = blocks,
            BibliographyEntries = new List<BibliographyEntry>(),
        };
    }

    private static string Latex(Document doc, string writer = "export", DocumentLook? lookOverride = null)
    {
        var tex = writer == "preview"
            ? new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc)
            : new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
                .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions { CompileHere = true, LookOverride = lookOverride });
        TexSourceGuard.Violation(tex).Should().BeNull("the guard must accept the generated deck");
        tex.Should().Contain(@"\documentclass[11pt,a4paper]{beamer}").And.Contain(@"\begin{frame}{The mixing matrix}")
            .And.Contain(@"\section{Data}").And.Contain(@"\section*{Aside}").And.Contain(@"\appendix");
        return tex;
    }

    private static async Task<byte[]> Compile(string tex, string engine) =>
        // Not tolerant: any LaTeX error fails the test.
        await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, engine, timeout: 180);

    public static TheoryData<string, string, bool> Matrix()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var theme in new[] { "cerulean", "index" })
            foreach (var engine in new[] { "pdflatex", "lualatex" })
                foreach (var printSafe in new[] { false, true })
                    data.Add(theme, engine, printSafe);
        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task A_deck_compiles_in_its_faces_and_colours(string theme, string engine, bool printSafe)
    {
        var doc = Doc(theme, engine);
        // Print-safe as Export PDF does it (the stored paper stays the theme's).
        var tex = Latex(doc, lookOverride: printSafe ? DocumentLook.Parse(doc.Look).With(null, printSafe: true) : null);
        var name = theme == "cerulean" ? "LiliaCerulean" : "LiliaIndex";
        tex.Should().Contain(printSafe ? $@"\usetheme[printsafe]{{{name}}}" : $@"\usetheme{{{name}}}").And.NotContain("{lilia-theme}");
        if (theme == "index") tex.Should().Contain(@"\liliaPinColour{2}{7}");
        else tex.Should().NotContain("liliaPinColour");

        var pdf = await Compile(tex, engine);

        // One space between words (a title may wrap), none around the kicker's "·" (LuaLaTeX's thin
        // spaces come out as spaces).
        var text = Flat(await Tool("pdftotext", pdf, "-"));
        foreach (var word in Words)
            text.Should().ContainEquivalentOf(word, $"{theme}/{engine}/printsafe={printSafe} must keep the deck (Index sets titles in capitals)");
        text.Should().Contain("A neutrino is a light lepton");

        var fonts = await Tool("pdffonts", pdf);
        fonts.Should().Contain("Montserrat-Medium", "the body is Montserrat 500");
        fonts.Should().Contain(theme == "index" ? "Montserrat-ExtraBold" : "Montserrat-Bold", "the titles");
        fonts.Should().Contain("SourceSerif4-It", "a definition is set in Source Serif italic");
        fonts.Should().NotContain("JosefinSans").And.NotContain("LMSans", "no Exposition face, no substitute");

        var colours = FillColours(pdf);
        if (theme == "cerulean")
        {
            colours.Should().Contain(c => Near(c, CeruleanTitle), "frame titles and the foot tab");
            colours.Should().Contain(c => Near(c, CeruleanAccent), "the kicker tab");
            colours.Should().Contain(c => Near(c, CeruleanInk), "the body ink");
            colours.Should().Contain(c => Near(c, Danger), "the alertblock's title");
            // The title frame: full-bleed #08597F with white type; print-safe, white.
            AssertPixel(await Pixel(pdf, 1, 0.02, 0.02), printSafe ? White : CeruleanTitle, "the title frame's ground");
            AssertPixel(await Pixel(pdf, 4, 0.02, 0.5), White, "every other frame is on white");
            text.Should().Contain("1·3", "the kicker: section · frame").And.Contain("A·9", "appendix sections are lettered");
        }
        else
        {
            await AssertIndexSections(pdf);
            colours.Should().Contain(c => Near(c, Danger), "alertblock keeps red");
            colours.Should().Contain(c => Near(c, Tint(Danger, 8)), "on its 8 % tint");
            text.Should().Contain("1·3").And.Contain("2·6").And.Contain("3·7").And.Contain("A·9");
        }
    }

    /// <summary>
    /// Index's colour per section, read from each frame's rendered page: section 1 the first
    /// colour, section 2 pinned to the eighth, section 3 the second (the third is never used), the
    /// starred section neutral, the appendix restarting at the first. Blocks take the section colour
    /// and its tint; the contents rows take their sections' colours.
    /// </summary>
    private static async Task AssertIndexSections(byte[] pdf)
    {
        var pages = await PageTexts(pdf);
        async Task<HashSet<int>> On(string word) => await PageColours(pdf, pages.FindIndex(t => t.Contains(word)) + 1);

        Has(await On("Quartz zebra"), Seq[0]).Should().BeTrue("section 1 takes the first colour");
        Has(await On("Muted owl"), Seq[7]).Should().BeTrue("section 2 is pinned to the eighth");
        Has(await On("Jovial wombat"), Seq[1]).Should().BeTrue("section 3 takes the next colour no pin took");
        var aside = await On("Fizzing yak");
        Has(aside, Neutral).Should().BeTrue("a starred section is neutral charcoal");
        new[] { Seq[0], Seq[7], Seq[1] }.Should().NotContain(c => Has(aside, c), "no section is current there");
        Has(await On("Velvet heron"), Seq[0]).Should().BeTrue("the appendix restarts the sequence");

        var blocks = await On("Careful with signs");
        Has(blocks, Seq[0]).Should().BeTrue("the block's title bar is the section colour");
        Has(blocks, Tint(Seq[0], 8)).Should().BeTrue("its body on the 8 % tint");
        Has(blocks, Danger).Should().BeTrue("alertblock keeps red");

        var contents = await On("OUTLINE");
        new[] { Seq[0], Seq[7], Seq[1] }.Should().OnlyContain(c => Has(contents, c), "each contents row in its section's colour");

        FillColours(pdf).Should().NotContain(c => Near(c, Seq[2]), "the pin replaces the third colour, which nothing uses");
    }

    [Fact]
    public async Task A_deck_without_a_quotation_or_definition_has_no_serif_face()
    {
        var pdf = await Compile(Latex(Doc("cerulean", "pdflatex", definition: false)), "pdflatex");
        var fonts = await Tool("pdffonts", pdf);
        fonts.Should().Contain("Montserrat").And.NotContain("SourceSerif");
    }

    [Theory]
    [InlineData("cerulean")]
    [InlineData("index")]
    public async Task The_preview_writer_compiles_the_deck_too(string theme)
    {
        var tex = Latex(Doc(theme, "pdflatex"), "preview");
        tex.Should().Contain(theme == "index" ? @"\usetheme{LiliaIndex}" : @"\usetheme{LiliaCerulean}");
        var text = await Tool("pdftotext", await Compile(tex, "pdflatex"), "-");
        text.Should().Contain("Quartz zebra").And.Contain("Velvet heron");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Index_divider_frames_when_the_author_turns_them_on(bool printSafe)
    {
        // Off by default (no frame is added above); the author's \AtBeginSection turns them on.
        var doc = Doc("index", "pdflatex", paper: printSafe ? "white" : "theme",
            preamble: @"\AtBeginSection[]{\begin{frame}\sectionpage\end{frame}}");
        var pdf = await Compile(Latex(doc), "pdflatex");
        var pages = await PageTexts(pdf);
        // Title, contents, then the first section's divider.
        pages[2].Should().Contain("MIXING").And.Contain("1").And.NotContain("Quartz zebra");
        if (printSafe)
        {
            AssertPixel(await Pixel(pdf, 3, 0.5, 0.5), White, "print-safe dividers are white");
            AssertPixel(await Pixel(pdf, 3, 0.01, 0.5), Seq[0], "with a 6 mm bar in the section colour at the left edge");
        }
        else
        {
            AssertPixel(await Pixel(pdf, 3, 0.5, 0.05), Seq[0], "full-bleed in the section colour");
            AssertPixel(await Pixel(pdf, 3, 0.98, 0.02), Seq[0], "full-bleed to the corner");
        }
        // The pinned section's divider takes the pin.
        var data = pages.FindIndex(p => p.Contains("DATA") && !p.Contains("Muted owl"));
        data.Should().BePositive();
        AssertPixel(await Pixel(pdf, data + 1, printSafe ? 0.01 : 0.5, 0.5), Seq[7], "the pinned section's divider");
    }

    /// <summary>
    /// Index's title frame (Olivia, 6 Oct 2026): the title stays charcoal, since no section owns
    /// the frame, and the section bar at the top shows every section at full colour, a preview of
    /// the deck's index as the booklet's contents page is. The next frame, still before the first
    /// section, goes back to the 30 % tints.
    /// </summary>
    [Fact]
    public async Task Index_title_frame_shows_every_section_at_full_colour()
    {
        var pdf = await Compile(Latex(Doc("index", "pdflatex")), "pdflatex");
        // Three numbered sections before \appendix: the first colour, the pin (eighth), the second.
        string[] sections = [Seq[0], Seq[7], Seq[1]];
        for (var i = 0; i < sections.Length; i++)
        {
            AssertPixel(await BarPixel(pdf, 1, (i + 0.5) / sections.Length), sections[i],
                $"the title frame's bar shows section {i + 1} at full colour");
            AssertPixel(await BarPixel(pdf, 2, (i + 0.5) / sections.Length), Tint(sections[i], 30),
                $"the contents frame's bar keeps section {i + 1} at 30 %");
        }
        Has(await PageColours(pdf, 1), Neutral).Should().BeTrue("the title is charcoal");
    }

    // ── reading the PDF ──────────────────────────────────────────────────

    /// <summary>
    /// The colour of the section bar (3 pt high at the very top) at a fraction of the page width:
    /// the page rendered at 144 dpi, two pixels a point, read 1 pt from the top.
    /// </summary>
    private static async Task<string> BarPixel(byte[] pdf, int page, double x)
    {
        var (w, _, rgb) = await Render(pdf, page, 144);
        var px = Math.Clamp((int)(x * w), 0, w - 1);
        var o = 3 * (2 * w + px);
        return $"#{rgb[o]:X2}{rgb[o + 1]:X2}{rgb[o + 2]:X2}";
    }

    private static string Flat(string text) =>
        System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " "), @" ?· ?", "·");

    /// <summary><paramref name="hex"/> at <paramref name="percent"/> % over white, as xcolor mixes it.</summary>
    private static string Tint(string hex, int percent)
    {
        string Ch(int i)
        {
            var v = Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16) / 255.0;
            return ((int)Math.Round((v * percent / 100.0 + (1 - percent / 100.0)) * 255)).ToString("X2");
        }
        return "#" + Ch(0) + Ch(1) + Ch(2);
    }

    private static void AssertPixel(string actual, string expected, string because)
    {
        int Ch(string hex, int i) => Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16);
        Enumerable.Range(0, 3).All(i => Math.Abs(Ch(actual, i) - Ch(expected, i)) <= 2)
            .Should().BeTrue($"{because}: expected {expected}, the page shows {actual}");
    }

    /// <summary>Each page's text, in order (pdftotext ends every page with a form feed).</summary>
    private static async Task<List<string>> PageTexts(byte[] pdf) =>
        (await Tool("pdftotext", pdf, "-")).Split('\f').SkipLast(1).ToList();

    /// <summary>Whether a rendered page shows this colour (within 2/255 a channel).</summary>
    private static bool Has(HashSet<int> colours, string hex)
    {
        int Ch(int i) => Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16);
        for (var dr = -2; dr <= 2; dr++)
            for (var dg = -2; dg <= 2; dg++)
                for (var db = -2; db <= 2; db++)
                    if (colours.Contains(((Ch(0) + dr) << 16) | ((Ch(1) + dg) << 8) | (Ch(2) + db))) return true;
        return false;
    }

    /// <summary>Every colour on one page rendered at 60 dpi.</summary>
    private static async Task<HashSet<int>> PageColours(byte[] pdf, int page)
    {
        page.Should().BePositive("the page must exist");
        var (w, h, rgb) = await Render(pdf, page, 60);
        var set = new HashSet<int>();
        for (var i = 0; i < w * h; i++) set.Add((rgb[3 * i] << 16) | (rgb[3 * i + 1] << 8) | rgb[3 * i + 2]);
        return set;
    }

    /// <summary>The colour of one pixel of a page rendered at 40 dpi, at a fraction of its width and height.</summary>
    private static async Task<string> Pixel(byte[] pdf, int page, double x, double y)
    {
        var (w, h, rgb) = await Render(pdf, page, 40);
        int px = Math.Clamp((int)(x * w), 0, w - 1), py = Math.Clamp((int)(y * h), 0, h - 1);
        var o = 3 * (py * w + px);
        return $"#{rgb[o]:X2}{rgb[o + 1]:X2}{rgb[o + 2]:X2}";
    }

    /// <summary>One page as RGB pixels (pdftoppm, binary PPM).</summary>
    private static async Task<(int W, int H, byte[] Rgb)> Render(byte[] pdf, int page, int dpi)
    {
        var dir = Directory.CreateTempSubdirectory("lilia-beamer-render").FullName;
        try
        {
            var path = Path.Combine(dir, "deck.pdf");
            await File.WriteAllBytesAsync(path, pdf);
            await Run("pdftoppm", "-r", dpi.ToString(), "-f", page.ToString(), "-l", page.ToString(), "-singlefile", path, Path.Combine(dir, "page"));
            var ppm = await File.ReadAllBytesAsync(Path.Combine(dir, "page.ppm"));
            // P6 <width> <height> <max>, one whitespace, then the RGB bytes.
            var pos = 0;
            string Token()
            {
                while (char.IsWhiteSpace((char)ppm[pos])) pos++;
                var start = pos;
                while (!char.IsWhiteSpace((char)ppm[pos])) pos++;
                return System.Text.Encoding.ASCII.GetString(ppm, start, pos - start);
            }
            Token().Should().Be("P6");
            int w = int.Parse(Token()), h = int.Parse(Token());
            Token();
            pos++;
            return (w, h, ppm[pos..(pos + 3 * w * h)]);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static async Task Run(string tool, params string[] args)
    {
        var psi = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        p.ExitCode.Should().Be(0, $"{tool} must succeed");
    }
}
