using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// MCP hardening T4 (cloud session's handoff, 12 Oct): inputs that failed the whole compile.
/// Each is compiled for real, through the check emitter and the engine it picks.
/// </summary>
public class CompileBreakerTests
{
    private readonly RenderService _render = new(null!, NullLogger<RenderService>.Instance);

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private string RenderDoc(string? customPreamble, params Block[] blocks)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "T", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 11, LatexDocumentClass = "article", CustomPreamble = customPreamble,
            Blocks = blocks.Select((b, i) => { b.SortOrder = i; return b; }).ToList(),
            BibliographyEntries = new List<BibliographyEntry>(),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        foreach (var b in doc.Blocks) b.Document = doc;
        return _render.RenderToLatex(doc);
    }

    private static Task<byte[]> Compile(string tex) =>
        new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance)
            .RenderToPdfAsync(tex, EngineDetector.Detect(tex).ToCli(), timeout: 180);

    // ── MCP-17 ──────────────────────────────────────────────────────────
    [Theory]
    [InlineData("js")]
    [InlineData("javascript")]
    [InlineData("ts")]
    public void JavaScript_maps_to_a_language_the_preamble_defines(string lang)
    {
        _render.RenderBlockToLatex(B("code", new { code = "const x = 1;", language = lang }))
            .Should().Contain("language=JavaScript");
        LaTeXPreamble.Packages.Should().Contain(@"\lstdefinelanguage{JavaScript}");
        LaTeXPreamble.ValidationPackages.Should().Contain(@"\lstdefinelanguage{JavaScript}");
    }

    // ── MCP-18 ──────────────────────────────────────────────────────────
    [Fact]
    public void Non_ascii_in_code_goes_through_an_escape()
    {
        var latex = _render.RenderBlockToLatex(B("code", new { code = "print(\"Café Zürich\")", language = "python" }));
        latex.Should().Contain("escapeinside={(*@}{@*)}").And.Contain("Caf(*@é@*) Z(*@ü@*)rich");
        _render.RenderBlockToLatex(B("code", new { code = "x = 1", language = "python" })).Should().NotContain("escapeinside");
    }

    [Fact]
    public void The_escape_avoids_delimiters_the_code_uses() =>
        ListingText.Escape("s = \"(*@\" + \"é\"").Option.Should().Be("escapeinside={(*|}{|*)}");

    // ── MCP-26 ──────────────────────────────────────────────────────────
    [Fact]
    public void Cjk_text_picks_xelatex_and_loads_xecjk()
    {
        EngineDetector.Detect("数据").Should().Be(LatexEngine.Xelatex);
        RenderDoc(null, B("paragraph", new { text = "The word 数据 means data." })).Should().Contain(@"\usepackage{xeCJK}");
        RenderDoc(null, B("paragraph", new { text = "Plain text." })).Should().NotContain("xeCJK");
    }

    // ── MCP-44 ──────────────────────────────────────────────────────────
    [Fact]
    public void A_clashing_algorithm_package_is_left_out_of_the_custom_preamble()
    {
        var emitted = LaTeXPreamble.CustomPreambleForEmit("\\usepackage{algpseudocode}\n\\usepackage[x]{amsthm,algorithm2e}\n\\newcommand{\\R}{\\mathbb{R}}");
        emitted.Should().NotContain(@"\usepackage{algpseudocode}").And.Contain("% [lilia] left out algpseudocode");
        emitted.Should().Contain(@"\usepackage[x]{amsthm}").And.Contain(@"\newcommand{\R}{\mathbb{R}}");
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task The_campaign_inputs_compile()
    {
        var tex = RenderDoc("\\usepackage{algpseudocode}",
            B("paragraph", new { text = "Greek capitals: Ε, Α, Ω; small ς." }),
            B("code", new { code = "const city = \"Zürich\"; // café", language = "js" }),
            B("code", new { code = "print('naïve → ok')", language = "python" }));
        (await Compile(tex)).Should().NotBeEmpty();
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task A_document_with_chinese_and_japanese_compiles()
    {
        var tex = RenderDoc(null,
            B("heading", new { text = "数据 Résumé", level = 1 }),
            B("paragraph", new { text = "Chinese 数据, Japanese かな, Greek α and Ε, café, a · b — c…" }),
            B("code", new { code = "print(\"数 é\")", language = "python" }));
        (await Compile(tex)).Should().NotBeEmpty();
    }
}
