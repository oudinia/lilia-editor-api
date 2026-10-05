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
        else tex.Should().Contain($"\\usepackage[theme={theme}, paper=theme]{{lilia-theme}}");

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

    [Fact]
    public async Task A_planned_theme_named_directly_stops_the_compile()
    {
        // Only reachable by hand (PUT and the export refuse it), but the package must not quietly
        // print something else.
        var tex = Latex(Doc("article", "classic"), SampleBlocks())
            .Replace(@"\begin{document}", "\\usepackage[theme=carnet]{lilia-theme}\n\\begin{document}");
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
