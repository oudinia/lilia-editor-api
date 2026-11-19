using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Features.Export.Typst;

/// <summary>
/// The accessible PDF from Typst (launch-bot #259, 19 Nov 2026). An accessible PDF always went through LaTeX's tagged
/// PDF/UA-2, a minute or more of LuaLaTeX, even for a document whose PDF Typst makes. Typst 0.15 writes PDF/UA-1
/// (<c>--pdf-standard ua-1</c>) and refuses what the standard forbids: no title, an image or a drawn figure without alt
/// text, maths without alt text, a first heading below level 1 or a skipped level. The accessible build adds what Lilia
/// can: a title that is never empty, the language, and alt text on every figure (its description, else its caption,
/// else "Figure N"). The normal build must not change.
///
/// <para>The first tests read the emitted source; the last ones run Typst itself, like the other Typst compile tests
/// here, and fail (not skip) when it is not installed.</para>
/// </summary>
public class TypstAccessiblePdfTests
{
    // A 1×1 PNG, for a figure Typst can open.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private const string ImageUrl = "https://cdn.example.org/doc/figure-1.png";
    private const string StagedPath = "figures/figure-1.png";

    private int _order;

    private Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = _order++,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    private static Document Doc(string title = "River temperatures") => new()
    {
        Id = Guid.NewGuid(), Title = title, Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
    };

    private static TypstExportOptions Options(string? language = "en") => new()
    {
        LocalImagePaths = new Dictionary<string, string> { [ImageUrl] = StagedPath },
        HasBibliography = false,
        Language = language,
    };

    private List<Block> Report() =>
    [
        B("heading", new { text = "Method", level = 1 }),
        B("paragraph", new { text = "We measured the **water** every week." }),
        B("heading", new { text = "Sites", level = 2 }),
        B("figure", new { src = ImageUrl, caption = "The *three* sites", alt = "Map of three river sites along the valley" }),
        B("figure", new { src = ImageUrl, caption = "Weekly readings", alt = "IMG_2041.png" }),
        B("figure", new { src = "" }),
        B("table", new { headers = new[] { "Site", "°C" }, rows = new[] { new[] { "North", "4" }, new[] { "South", "19" } }, caption = "Averages" }),
        B("list", new { items = new[] { "One", "Two" }, ordered = true }),
        B("code", new { code = "mean = sum(xs) / len(xs)\n", language = "python" }),
    ];

    private static TypstAccessibleSource Accessible(Document doc, List<Block> blocks, string? language = "en") =>
        new TypstExportService().BuildAccessibleTypstDocument(doc, blocks, null, Options(language));

    // ────────── the emitted source ──────────

    [Fact]
    public void The_normal_Typst_source_is_unchanged_no_alt_text_and_no_language()
    {
        var normal = new TypstExportService().BuildTypstDocument(Doc(), Report(), null, Options());

        normal.Should().NotContain("alt:").And.NotContain("lang:");
        normal.Should().Contain($"#figure(image(\"{StagedPath}\"), caption:");
    }

    [Fact]
    public void The_accessible_source_says_the_documents_language()
    {
        Accessible(Doc(), Report()).Source.Should().Contain("#set text(lang: \"en\")");
        Accessible(Doc(), Report(), "en-GB").Source.Should().Contain("#set text(lang: \"en\", region: \"GB\")");
        Accessible(Doc(), Report(), "fr").Source.Should().Contain("#set text(lang: \"fr\")");
    }

    [Theory]
    [InlineData(null, "#set text(lang: \"en\")")]
    [InlineData("not a language", "#set text(lang: \"en\")")]
    [InlineData("de", "#set text(lang: \"de\")")]
    [InlineData("pt-BR", "#set text(lang: \"pt\", region: \"BR\")")]
    public void A_language_Typst_cannot_read_is_written_as_English(string? code, string expected) =>
        TypstExportService.TypstLanguageRule(code).Should().Be(expected);

    [Fact]
    public void A_document_without_a_title_gets_one_in_the_file_but_prints_none()
    {
        var source = Accessible(Doc(title: ""), Report()).Source;

        source.Should().Contain("#set document(title: \"Untitled document\")");
        source.Should().NotContain("#text(size: 1.6em, weight: \"bold\")[Untitled document]");
    }

    [Fact]
    public void Each_figure_carries_its_description_else_its_caption_else_its_number()
    {
        var source = Accessible(Doc(), Report()).Source;

        source.Should().Contain($"image(\"{StagedPath}\", alt: \"Map of three river sites along the valley\")");
        // A file name is not a description: the caption, as plain text.
        source.Should().Contain($"image(\"{StagedPath}\", alt: \"Weekly readings\")");
        // No image, no caption: the grey box carries "Figure 3" on the figure itself.
        source.Should().Contain("[Image placeholder]]], alt: \"Figure 3\")");
    }

    [Fact]
    public void The_caption_used_as_alt_text_loses_its_marks()
    {
        var blocks = new List<Block> { B("figure", new { src = ImageUrl, caption = "The **red** and \\textit{blue} curves" }) };

        Accessible(Doc(), blocks).Source.Should().Contain("alt: \"The red and blue curves\"");
    }

    [Fact]
    public void The_figure_word_follows_the_documents_language()
    {
        var blocks = new List<Block> { B("figure", new { src = ImageUrl }) };

        Accessible(Doc(), blocks, "de").Source.Should().Contain("alt: \"Abbildung 1\"");
    }

    [Fact]
    public void A_document_with_maths_is_reported_so_LaTeX_can_tag_it_as_MathML()
    {
        Accessible(Doc(), Report()).HasMaths.Should().BeFalse();
        Accessible(Doc(), [B("paragraph", new { text = "Energy is $E = mc^2$." })]).HasMaths.Should().BeTrue();
        Accessible(Doc(), [B("equation", new { source = "a^2 + b^2 = c^2", displayMode = true })]).HasMaths.Should().BeTrue();
    }

    [Fact]
    public void The_accessible_build_leaves_nothing_behind_for_the_next_normal_build()
    {
        var exporter = new TypstExportService();
        exporter.BuildAccessibleTypstDocument(Doc(), Report(), null, Options());

        exporter.BuildTypstDocument(Doc(), Report(), null, Options()).Should().NotContain("alt:").And.NotContain("lang:");
    }

    [Fact]
    public void A_refusal_is_read_from_Typsts_first_error_line()
    {
        PreviewRenderService.UaRefusal("error: PDF/UA-1 error: skipped from heading level 1 to 3\n  ┌─ main.typ:4:0\n")
            .Should().Be("PDF/UA-1 error: skipped from heading level 1 to 3");
        PreviewRenderService.UaRefusal(null).Should().Be("Typst could not make a PDF/UA-1 file");
    }

    // ────────── Typst itself ──────────

    private static Task<TypstCompileResult> CompileUa1(string source) =>
        new TypstCompileService().CompileAsync(source, TypstOutputFormat.Pdf, null,
            new Dictionary<string, byte[]> { [StagedPath] = Png }, evalAfter: null, TypstPdfStandard.Ua1);

    [Fact]
    public async Task Typst_makes_a_PDF_UA_1_file_from_the_accessible_source()
    {
        TypstEnvironment.Require();

        var result = await CompileUa1(Accessible(Doc(), Report()).Source);

        result.Success.Should().BeTrue(result.Error);
        var raw = System.Text.Encoding.Latin1.GetString(Inflated(result.Output!));
        raw.Should().Contain("pdfuaid").And.Contain("/StructTreeRoot").And.Contain("/Lang(en)")
            .And.Contain("/DisplayDocTitle true").And.Contain("/Figure").And.Contain("/Table");
        // The figures' alt text, as Typst writes plain ASCII: the first one's description, the third one's number.
        raw.Should().Contain("/Alt(Map of three river sites along the valley)").And.Contain("/Alt(Figure 3)");
    }

    [Fact]
    public async Task Typst_refuses_the_normal_source_as_PDF_UA_1_which_is_why_the_accessible_build_exists()
    {
        TypstEnvironment.Require();

        var normal = new TypstExportService().BuildTypstDocument(Doc(), Report(), null, Options());
        var result = await CompileUa1(normal);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("missing alt text");
    }

    [Fact]
    public async Task Typst_refuses_a_skipped_heading_level_so_that_document_goes_to_LaTeX()
    {
        TypstEnvironment.Require();

        var blocks = new List<Block>
        {
            B("heading", new { text = "Method", level = 1 }),
            B("heading", new { text = "Detail", level = 3 }),
        };
        var result = await CompileUa1(Accessible(Doc(), blocks).Source);

        result.Success.Should().BeFalse();
        PreviewRenderService.UaRefusal(result.Error).Should().Contain("skipped from heading level 1 to 3");
    }

    [Fact]
    public async Task A_normal_Typst_compile_passes_no_PDF_standard()
    {
        TypstEnvironment.Require();

        // The same source Typst refuses as PDF/UA-1 compiles as before without the flag.
        var normal = new TypstExportService().BuildTypstDocument(Doc(), Report(), null, Options());
        var result = await new TypstCompileService().CompileAsync(normal, TypstOutputFormat.Pdf, null,
            new Dictionary<string, byte[]> { [StagedPath] = Png });

        result.Success.Should().BeTrue(result.Error);
    }

    // The PDF with its compressed streams inflated beside it, to read its tags.
    private static byte[] Inflated(byte[] pdf)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        var output = new MemoryStream();
        output.Write(pdf);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "stream\r?\n"))
        {
            try
            {
                using var z = new System.IO.Compression.ZLibStream(new MemoryStream(pdf, m.Index + m.Length, pdf.Length - m.Index - m.Length), System.IO.Compression.CompressionMode.Decompress);
                var buffer = new byte[1 << 16];
                int n;
                while ((n = z.Read(buffer)) > 0) output.Write(buffer, 0, n);
            }
            catch (InvalidDataException) { }
        }
        return output.ToArray();
    }
}
