using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines.TexSafety;
using Lilia.Engines.Themes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Themes;

/// <summary>
/// The themes compiled for real: a sample document under Classic, Cerulean and Index, on article,
/// report and book, with pdfLaTeX and LuaLaTeX, through the same exporter and compile service the
/// PDF export uses (which stages lilia-theme.sty beside the .tex). Each PDF must contain the
/// document's words (pdftotext) and, when themed, be set in the theme's faces (pdffonts): a theme
/// never prints in a substitute face.
/// </summary>
[Trait("Category", "ThemeCompile")]
public class DocumentThemeCompileTests
{
    private static int _order;

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = _order++,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    private static List<Block> SampleBlocks() =>
    [
        B("tableofcontents", new { }),
        B("heading", new { text = "Eigenvalues", level = 1 }),
        B("paragraph", new { text = "Quartz zebra paragraph about eigenvalues, café included." }),
        B("heading", new { text = "Characteristic polynomial", level = 2 }),
        B("paragraph", new { text = "Jovial wombat paragraph with a matrix." }),
        B("table", new { caption = "Accuracy by model", headers = new[] { "Model", "Accuracy" }, rows = new[] { new[] { "Baseline", "76.1" }, new[] { "Ours", "82.6" } } }),
        B("heading", new { text = "Diagonalisation", level = 1 }),
        B("paragraph", new { text = "Fizzing yak paragraph closes the notes." }),
        B("heading", new { text = "Further reading", level = 1, numbered = false }),
        B("paragraph", new { text = "Muted owl paragraph after an unnumbered heading." }),
    ];

    private static readonly string[] Words = ["Quartz zebra", "Jovial wombat", "Fizzing yak", "Muted owl", "Eigenvalues", "Diagonalisation", "Baseline", "82.6"];

    private static Document Doc(string cls, string theme, string? customPreamble = null) => new()
    {
        Id = Guid.NewGuid(), Title = "Linear Algebra Notes", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
        LatexDocumentClass = cls,
        Look = theme == "classic" ? null : JsonSerializer.Serialize(new { theme, paper = "theme", pins = new { } }),
        CustomPreamble = customPreamble,
    };

    private static string Latex(Document doc, List<Block> blocks)
    {
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, blocks, [], new LaTeXExportOptions());
        TexSourceGuard.Violation(tex).Should().BeNull("the guard must accept the themed source");
        return tex;
    }

    private static async Task<byte[]> Compile(string tex, string engine) =>
        await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, engine, timeout: 180);

    public static TheoryData<string, string, string> Matrix()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var cls in new[] { "article", "report", "book" })
            foreach (var theme in new[] { "classic", "cerulean", "index" })
                foreach (var engine in new[] { "pdflatex", "lualatex" })
                    data.Add(cls, theme, engine);
        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task A_sample_document_compiles_and_keeps_its_content(string cls, string theme, string engine)
    {
        var tex = Latex(Doc(cls, theme), SampleBlocks());
        if (theme == "classic") tex.Should().NotContain("lilia-theme");
        else tex.Should().Contain(cls == "article"
            ? $"\\usepackage[theme={theme}, paper=theme]{{lilia-theme}}"
            // In report and book a level-1 heading prints as \chapter, the top level.
            : $"\\usepackage[theme={theme}, paper=theme, top=chapter]{{lilia-theme}}");

        var pdf = await Compile(tex, engine);

        var text = await Tool("pdftotext", pdf, "-");
        foreach (var word in Words) text.Should().Contain(word, $"{cls}/{theme}/{engine} must keep the content");

        var fonts = await Tool("pdffonts", pdf);
        if (theme == "classic")
        {
            fonts.Should().NotContain("Montserrat");
        }
        else
        {
            fonts.Should().Contain("Montserrat", "headings are set in the theme's structure face");
            fonts.Should().Contain("SourceSerif", "the body is set in the theme's reading face");
        }
    }

    [Theory]
    [InlineData("pdflatex")]
    [InlineData("lualatex")]
    public async Task The_authors_preamble_wins_over_the_themes_headings(string engine)
    {
        // A custom preamble that restyles \section with titlesec (the same package the theme
        // uses), and one that replaces it outright the old way. Both compile, and the author's
        // label format ("Part 1:") is what prints.
        const string restyled = @"\titleformat{\section}{\normalfont\Large\scshape}{Part \thesection:}{0.5em}{}";
        var pdf = await Compile(Latex(Doc("article", "index", restyled), SampleBlocks()), engine);
        // Small caps come out of pdftotext as capitals.
        (await Tool("pdftotext", pdf, "-")).Should().ContainEquivalentOf("Part 1: Eigenvalues").And.Contain("Quartz zebra");

        const string replaced = @"\makeatletter
\renewcommand\section{\@startsection{section}{1}{\z@}{-3ex}{1.5ex}{\normalfont\large\itshape}}
\makeatother";
        pdf = await Compile(Latex(Doc("report", "cerulean", replaced), SampleBlocks()), engine);
        (await Tool("pdftotext", pdf, "-")).Should().Contain("Diagonalisation").And.Contain("Fizzing yak");
    }

    [Fact]
    public async Task Index_pins_and_print_safe_compile()
    {
        var blocks = SampleBlocks();
        var second = blocks.First(b => b.Type == "heading" && b.Content.RootElement.GetProperty("text").GetString() == "Diagonalisation");
        var doc = Doc("article", "index");
        doc.Look = JsonSerializer.Serialize(new { theme = "index", paper = "white", pins = new Dictionary<string, int> { [second.Id.ToString()] = 7 } });

        var tex = Latex(doc, blocks);
        tex.Should().Contain(@"\liliaPinColour{2}{7}");

        var pdf = await Compile(tex, "pdflatex");
        (await Tool("pdftotext", pdf, "-")).Should().Contain("Fizzing yak");
    }

    [Theory]
    [InlineData("report")]
    [InlineData("book")]
    public async Task Index_colours_the_level_1_headings_of_a_report_or_book_and_honours_a_pin(string cls)
    {
        // Three level-1 headings (printed as \chapter) and a pin on the third: 1 takes the first
        // colour, 2 the second, 3 the pinned eighth. The colours are read from the PDF's content
        // streams, so this checks what prints, not what the source says.
        var blocks = new List<Block>
        {
            B("heading", new { text = "Vectors", level = 1 }),
            B("paragraph", new { text = "Quartz zebra paragraph." }),
            B("heading", new { text = "Matrices", level = 1 }),
            B("paragraph", new { text = "Jovial wombat paragraph." }),
            B("heading", new { text = "Eigenvalues", level = 1 }),
            B("paragraph", new { text = "Fizzing yak paragraph." }),
        };
        var doc = Doc(cls, "index");
        doc.Look = JsonSerializer.Serialize(new { theme = "index", paper = "theme", pins = new Dictionary<string, int> { [blocks[4].Id.ToString()] = 7 } });

        var tex = Latex(doc, blocks);
        tex.Should().Contain("top=chapter").And.Contain(@"\liliaPinColour{3}{7}");

        var pdf = await Compile(tex, "pdflatex");

        var text = await Tool("pdftotext", pdf, "-");
        text.Should().Contain("Vectors").And.Contain("Matrices").And.Contain("Eigenvalues").And.Contain("Fizzing yak");

        var colours = FillColours(pdf);
        colours.Should().Contain(c => Near(c, "#4A5FA3"), "heading 1 takes the first colour");
        colours.Should().Contain(c => Near(c, "#996300"), "heading 2 takes the second");
        colours.Should().Contain(c => Near(c, "#2E6E9E"), "heading 3 is pinned to the eighth");
        colours.Should().NotContain(c => Near(c, "#B8303A"), "the pin replaces the third colour, which nothing else uses");
    }

    /// <summary>Every non-stroking RGB colour (<c>r g b rg</c>) set in the PDF's content streams.</summary>
    private static List<(double R, double G, double B)> FillColours(byte[] pdf)
    {
        var found = new List<(double, double, double)>();
        var raw = System.Text.Encoding.Latin1.GetString(pdf);
        var streams = System.Text.RegularExpressions.Regex.Matches(raw, @"stream\r?\n");
        foreach (System.Text.RegularExpressions.Match m in streams)
        {
            var start = m.Index + m.Length;
            var end = raw.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0) continue;
            string content;
            try
            {
                using var input = new MemoryStream(pdf, start, end - start);
                using var z = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
                using var reader = new StreamReader(z, System.Text.Encoding.Latin1);
                content = reader.ReadToEnd();
            }
            catch
            {
                content = raw[start..end];
            }
            foreach (System.Text.RegularExpressions.Match c in System.Text.RegularExpressions.Regex.Matches(
                         content, @"(?<![\d.])(\d*\.?\d+) (\d*\.?\d+) (\d*\.?\d+) rg\b"))
                found.Add((double.Parse(c.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                           double.Parse(c.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                           double.Parse(c.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)));
        }
        return found;
    }

    private static bool Near((double R, double G, double B) c, string hex)
    {
        double Ch(int i) => Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16) / 255.0;
        return Math.Abs(c.R - Ch(0)) < 0.004 && Math.Abs(c.G - Ch(1)) < 0.004 && Math.Abs(c.B - Ch(2)) < 0.004;
    }

    [Fact]
    public async Task A_planned_theme_named_directly_stops_the_compile()
    {
        // Only reachable by hand (PUT and the export refuse it), but the package must not quietly
        // print something else.
        var tex = Latex(Doc("article", "classic"), SampleBlocks())
            .Replace(@"\begin{document}", "\\usepackage[theme=exposition]{lilia-theme}\n\\begin{document}");
        var act = () => Compile(tex, "pdflatex");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>Run a poppler tool on the PDF and return its standard output.</summary>
    private static async Task<string> Tool(string tool, byte[] pdf, string? output = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lilia-theme-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, pdf);
        try
        {
            var psi = new ProcessStartInfo(tool)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            psi.ArgumentList.Add(path);
            if (output is not null) psi.ArgumentList.Add(output);
            using var p = Process.Start(psi)!;
            var stdout = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return stdout;
        }
        finally { File.Delete(path); }
    }
}
